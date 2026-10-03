#nullable enable
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using Microsoft.Data.Sqlite;

/// <summary>
/// Uploads the sealed outbox through <see cref="BundleUploadFeed.Session"/> against a recording
/// Mod API handler that answers like the server: transient failures keep file and row, an
/// accepted upload commits before its file is deleted, and the cleanup pass reclaims leftovers.
/// </summary>
internal static class UploadStage
{
    internal static async Task RunAsync(
        string root,
        PipelineArtifacts artifacts,
        IReadOnlyList<string> runIds
    )
    {
        var database = PathConstants.RunLogDatabase(root);
        var outboxRoot = PathConstants.BundleOutbox(root);
        var store = new BundleQueueStore(database);
        var services = new TestServices(new TestPaths(root));
        var all = runIds.ToArray();

        // A 5xx keeps the file and the pending row; the retry is due at once (interval 0).
        var unavailable = new ServerHandler(HttpStatusCode.ServiceUnavailable);
        using (var session = Session(services, store, unavailable, Files(outboxRoot)))
            await session.RunAttemptAsync(CancellationToken.None);
        Record(artifacts, "transient", unavailable);
        Check.That(unavailable.Requests.Count == BundleUploadFeed.MaximumAttemptBatch, "batch");
        foreach (var fileName in OutboxFiles(database, "pending", attempts: 1))
            Check.That(
                File.Exists(Path.Combine(outboxRoot, fileName)),
                "A transient response must never delete the outbox file."
            );
        artifacts.RecordQueue("upload:transient", database, all);

        // The uploaded state commits before the file is deleted: a delete that fails leaves an
        // uploaded row with its file still present, for the next cleanup pass to reclaim.
        var accepted = new ServerHandler(HttpStatusCode.Created);
        var failingDelete = new DeleteFailingFiles(Files(outboxRoot));
        using (var session = Session(services, store, accepted, failingDelete))
            await session.RunAttemptAsync(CancellationToken.None);
        Record(artifacts, "commit-before-delete", accepted);
        Check.That(failingDelete.DeleteCalls == 3, "Each accepted upload should delete its file.");
        var leftovers = OutboxFiles(database, "uploaded", attempts: 1);
        Check.That(leftovers.Count == 3, "Accepted uploads must commit before their file delete.");
        foreach (var fileName in leftovers)
            Check.That(
                File.Exists(Path.Combine(outboxRoot, fileName)),
                "A failed delete must leave the file behind the committed uploaded row."
            );
        artifacts.RecordQueue("upload:commit-before-delete", database, all);

        // Drain: cleanup reclaims the leftovers, then the transient rows upload and delete.
        var drain = new ServerHandler(HttpStatusCode.Created);
        var drainSession = Session(services, store, drain, Files(outboxRoot));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = await drainSession.RunAttemptAsync(CancellationToken.None);
            if (result.Observations.Any(o => o.Kind == UploadAttemptObservationKind.NoWork))
                break;
        }
        Record(artifacts, "drain", drain);
        Check.That(
            OutboxFiles(database, "uploaded", attempts: null).Count == all.Length,
            "Every sealed Bundle should end uploaded."
        );
        Check.That(
            !Directory.EnumerateFiles(outboxRoot, "*.bundle").Any(),
            "Uploaded files, including delete leftovers, should be reclaimed."
        );
        drainSession.Dispose();
        Check.That(drain.Disposed, "The upload session must dispose the transport it owns.");
        artifacts.RecordQueue("upload:drain", database, all);
    }

    private static BundleUploadFeed.Session Session(
        TestServices services,
        BundleQueueStore store,
        HttpMessageHandler handler,
        IBundleOutboxFiles files
    ) =>
        new(
            services,
            0,
            ModApiSession.TryCreate(
                "https://example.test",
                "test",
                "BundleUpload",
                TimeSpan.FromSeconds(30),
                handler
            ) ?? throw new InvalidOperationException("test Mod API session"),
            store,
            files
        );

    private static SystemBundleOutboxFiles Files(string outboxRoot) => new(outboxRoot);

    private static void Record(PipelineArtifacts artifacts, string step, ServerHandler handler)
    {
        foreach (var upload in handler.Requests)
            artifacts.RecordUpload(step, upload);
    }

    private static List<string> OutboxFiles(string database, string status, int? attempts)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT file_name FROM bundle_outbox WHERE status = $status AND ($attempts IS NULL OR attempts = $attempts);";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$attempts", (object?)attempts ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    /// <summary>Answers each upload like the server, echoing the Bundle's own identity.</summary>
    private sealed class ServerHandler(HttpStatusCode status) : HttpMessageHandler
    {
        internal List<RecordedUpload> Requests { get; } = new();
        internal bool Disposed { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(
                new RecordedUpload(
                    request.Method.Method,
                    request.RequestUri!.AbsolutePath,
                    request.Content.Headers.ContentType!.ToString(),
                    Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
                    body.Length,
                    (int)status
                )
            );
            Check.That(
                request.Content.Headers.ContentLength == body.Length,
                "Content-Length required"
            );
            Check.That(
                request.Headers.GetValues("Content-Digest").Single()
                    == BundleV5Codec.FormatContentDigest(body),
                "Content-Digest must cover the exact body"
            );
            var manifest = BundleV5Codec.Open(body).Manifest;
            var json =
                status == HttpStatusCode.Created
                    ? $"{{\"bundle_id\":\"{manifest.BundleId}\",\"run_id\":\"{manifest.Run.RunId}\",\"outcome\":\"stored\",\"bazaardb_delivery\":\"not_applicable\"}}"
                    : "{\"error\":{\"code\":\"storage_unavailable\",\"message\":\"later "
                        + LogPrivacy.ResponseSecret
                        + " "
                        + LogPrivacy.AccountSecret
                        + "\",\"retryable\":true,\"request_id\":\"r\"}}";
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class DeleteFailingFiles(IBundleOutboxFiles inner) : IBundleOutboxFiles
    {
        internal int DeleteCalls { get; private set; }

        public Stream OpenRead(string fileName) => inner.OpenRead(fileName);

        public bool Exists(string fileName) => inner.Exists(fileName);

        public long GetLength(string fileName) => inner.GetLength(fileName);

        public void Delete(string fileName)
        {
            DeleteCalls++;
            throw new IOException("outbox file is locked");
        }

        public IReadOnlyList<string> EnumerateBundleFileNames() => inner.EnumerateBundleFileNames();
    }
}

/// <summary>
/// Upload-feed rules the sealed pipeline cannot reach with real files: the 512 MB soft limit,
/// the outbox root boundary, and the live-run gate.
/// </summary>
internal static class UploadFeedBoundaries
{
    internal static async Task RunAsync(string root)
    {
        await CleanupRetentionAndSoftLimitStopAtThreshold(Path.Combine(root, "cleanup"));
        ProductionFilePortRejectsPathsOutsideItsRoot(Path.Combine(root, "file-port"));
        LiveRunDefersUploadAndArmWakesIt();
    }

    private static void LiveRunDefersUploadAndArmWakesIt()
    {
        var gate = new StartupUploadAttemptGate(20, 180);
        Check.That(
            gate.Poll(19, liveRunActive: false) == StartupUploadAttemptDecision.Wait,
            "The startup delay must be honored."
        );
        Check.That(
            gate.Poll(20, liveRunActive: true) == StartupUploadAttemptDecision.SkipLiveRun,
            "A live run must defer upload."
        );
        Check.That(
            gate.Poll(20, liveRunActive: false) == StartupUploadAttemptDecision.Start,
            "The first eligible attempt must start."
        );
        gate.ArmImmediateAttempt(21);
        Check.That(
            gate.Poll(21, liveRunActive: false) == StartupUploadAttemptDecision.Start,
            "An arm signal must wake the feed before its retry interval."
        );
    }

    private static void ProductionFilePortRejectsPathsOutsideItsRoot(string root)
    {
        var files = new SystemBundleOutboxFiles(root);
        try
        {
            files.Delete(Path.Combine("..", "outside.bundle"));
            throw new InvalidOperationException("A traversal path escaped the Bundle outbox root.");
        }
        catch (InvalidDataException) { }
    }

    // Row times are relative to the clock: retention windows are measured from now.
    private static async Task CleanupRetentionAndSoftLimitStopAtThreshold(string root)
    {
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "queue.db");
        var store = new BundleQueueStore(database);
        using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            connection.Open();
            InsertCleanupRow(connection, "uploaded", "uploaded", -1, null);
            InsertCleanupRow(connection, "old-failure", "permanent_failure", -10, -8);
            InsertCleanupRow(connection, "expired-pending", "pending", -20, null);
            InsertCleanupRow(connection, "reclaim-a", "permanent_failure", -3, 0);
            InsertCleanupRow(connection, "reclaim-b", "permanent_failure", -2, 0);
            InsertCleanupRow(connection, "reclaim-c", "permanent_failure", -1, 0);
        }

        const long big = 300L * 1024 * 1024;
        var files = new SizedFiles(
            new Dictionary<string, long>
            {
                ["uploaded.bundle"] = 10,
                ["old-failure.bundle"] = 10,
                ["expired-pending.bundle"] = 10,
                ["reclaim-a.bundle"] = big,
                ["reclaim-b.bundle"] = big,
                ["reclaim-c.bundle"] = big,
            }
        );
        using var session = new BundleUploadFeed.Session(
            new TestServices(new TestPaths(root)),
            180,
            ModApiSession.TryCreate(
                "https://example.test",
                "test",
                "CleanupTest",
                TimeSpan.FromSeconds(5),
                new NoRequestHandler()
            )!,
            store,
            files
        );

        await session.RunAttemptAsync(CancellationToken.None);

        Check.That(!files.Exists("uploaded.bundle"), "Uploaded files are deleted immediately.");
        Check.That(!files.Exists("old-failure.bundle"), "Seven-day permanent files are deleted.");
        Check.That(
            !files.Exists("expired-pending.bundle"),
            "Fourteen-day pending files expire and become reclaimable."
        );
        Check.That(
            !files.Exists("reclaim-a.bundle") && !files.Exists("reclaim-b.bundle"),
            "Soft-limit cleanup reclaims the oldest candidates."
        );
        Check.That(
            files.Exists("reclaim-c.bundle"),
            "Soft-limit cleanup must stop once total bytes reach the threshold."
        );
    }

    private static void InsertCleanupRow(
        SqliteConnection connection,
        string bundleId,
        string status,
        int sealedDays,
        int? failedDays
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO bundle_outbox (bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes, has_screenshot, sealed_at_utc, status, next_attempt_at_utc, failed_at_utc) VALUES ($bundleId, $runId, $fileName, 'sha', 'digest', 10, 0, $sealedAt, $status, $sealedAt, $failedAt);";
        command.Parameters.AddWithValue("$bundleId", bundleId);
        command.Parameters.AddWithValue("$runId", "run-" + bundleId);
        command.Parameters.AddWithValue("$fileName", bundleId + ".bundle");
        command.Parameters.AddWithValue(
            "$sealedAt",
            DateTimeOffset.UtcNow.AddDays(sealedDays).ToString("o")
        );
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue(
            "$failedAt",
            failedDays.HasValue
                ? DateTimeOffset.UtcNow.AddDays(failedDays.Value).ToString("o")
                : DBNull.Value
        );
        command.ExecuteNonQuery();
    }

    // Lengths stand in for files too large to write: three of them cross the 512 MB soft limit.
    private sealed class SizedFiles(Dictionary<string, long> files) : IBundleOutboxFiles
    {
        public Stream OpenRead(string fileName) =>
            throw new InvalidOperationException("Cleanup should not open files.");

        public bool Exists(string fileName) => files.ContainsKey(fileName);

        public long GetLength(string fileName) => files[fileName];

        public void Delete(string fileName) => files.Remove(fileName);

        public IReadOnlyList<string> EnumerateBundleFileNames() => files.Keys.ToArray();
    }

    private sealed class NoRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Cleanup has no due upload.");
    }
}
