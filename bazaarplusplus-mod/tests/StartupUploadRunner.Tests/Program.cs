#nullable enable
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using Microsoft.Data.Sqlite;

Assert(
    Enum.GetValues<UploadFeedKind>().SequenceEqual([UploadFeedKind.Bundle]),
    "V5 must expose exactly one upload feed kind."
);
Assert(
    typeof(IUploadFeed).IsAssignableFrom(typeof(BundleUploadFeed)),
    "The bundle upload feed must implement the shared feed contract."
);
Assert(BundleUploadFeed.MaximumAttemptBatch == 3, "Each attempt must upload at most 3 bundles.");

var cadence = new UploadPumpCadence(20, 180);
Assert(cadence.StartupDelaySeconds == 20, "Startup cadence drifted.");
Assert(cadence.RetryIntervalSeconds == 180, "Retry cadence drifted.");

var gate = new StartupUploadAttemptGate(cadence.StartupDelaySeconds, cadence.RetryIntervalSeconds);
Assert(
    gate.Poll(19, liveRunActive: false) == StartupUploadAttemptDecision.Wait,
    "Startup delay must be honored."
);
Assert(
    gate.Poll(20, liveRunActive: true) == StartupUploadAttemptDecision.SkipLiveRun,
    "Live runs must defer upload."
);
Assert(
    gate.Poll(20, liveRunActive: false) == StartupUploadAttemptDecision.Start,
    "The first eligible attempt must start."
);
Assert(
    gate.Poll(199, liveRunActive: false) == StartupUploadAttemptDecision.Wait,
    "Retry interval must be fixed at 180 seconds."
);
Assert(
    gate.Poll(200, liveRunActive: false) == StartupUploadAttemptDecision.Start,
    "The retry attempt must become eligible."
);

var armed = new StartupUploadAttemptGate(20, 180);
armed.ArmImmediateAttempt(3);
Assert(
    armed.Poll(3, liveRunActive: false) == StartupUploadAttemptDecision.Start,
    "An arm signal must wake the single feed."
);

await TestUploadedStateCommitsBeforeFileDeleteAndSessionOwnsTransport();
await TestTransientResponseNeverDeletesOutboxFile();
await TestCleanupRetentionAndSoftLimitStopAtThreshold();
TestProductionFilePortRejectsPathsOutsideItsRoot();

Console.WriteLine("Startup upload runner tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void TestProductionFilePortRejectsPathsOutsideItsRoot()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "bpp-outbox-file-port-tests",
        Guid.NewGuid().ToString("N")
    );
    try
    {
        var files = new SystemBundleOutboxFiles(root);
        try
        {
            files.Delete(Path.Combine("..", "outside.bundle"));
            throw new InvalidOperationException("A traversal path escaped the Bundle outbox root.");
        }
        catch (InvalidDataException) { }
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

static async Task TestUploadedStateCommitsBeforeFileDeleteAndSessionOwnsTransport()
{
    await WithSession(
        HttpStatusCode.Created,
        """{"bundle_id":"bundle-a","run_id":"run-a","outcome":"stored","bazaardb_delivery":"created"}""",
        async fixture =>
        {
            var statusAtDelete = string.Empty;
            fixture.Files.OnDelete = _ => statusAtDelete = fixture.Status("bundle-a");

            var result = await fixture.Session.RunAttemptAsync(CancellationToken.None);

            Assert(
                result.Observations.Any(observation =>
                    observation.Kind == UploadAttemptObservationKind.Succeeded
                ),
                "An uploaded bundle should produce a success observation."
            );
            Assert(
                statusAtDelete == "uploaded",
                "The DB commit must happen before sidecar deletion."
            );
            Assert(
                fixture.Files.DeleteCalls == 1,
                "Uploaded sidecar should be deleted exactly once."
            );
            fixture.Session.Dispose();
            Assert(
                fixture.Handler.Disposed,
                "The activation session must dispose its own transport."
            );
        }
    );
}

static async Task TestTransientResponseNeverDeletesOutboxFile()
{
    await WithSession(
        HttpStatusCode.ServiceUnavailable,
        """{"error":{"code":"busy","retryable":true}}""",
        async fixture =>
        {
            await fixture.Session.RunAttemptAsync(CancellationToken.None);

            Assert(fixture.Status("bundle-a") == "pending", "Transient failures remain pending.");
            Assert(
                fixture.Files.DeleteCalls == 0,
                "Transient failures must never delete the sidecar."
            );
            fixture.Session.Dispose();
        }
    );
}

static async Task TestCleanupRetentionAndSoftLimitStopAtThreshold()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "bpp-upload-cleanup-tests",
        Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(root);
    try
    {
        var database = Path.Combine(root, "queue.db");
        var store = new BundleQueueStore(database);
        using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            connection.Open();
            InsertCleanupRow(connection, "uploaded", "uploaded.bundle", "uploaded", -1, null);
            InsertCleanupRow(
                connection,
                "old-failure",
                "old-failure.bundle",
                "permanent_failure",
                -10,
                -8
            );
            InsertCleanupRow(
                connection,
                "expired-pending",
                "expired-pending.bundle",
                "pending",
                -20,
                null
            );
            InsertCleanupRow(
                connection,
                "reclaim-a",
                "reclaim-a.bundle",
                "permanent_failure",
                -3,
                0
            );
            InsertCleanupRow(
                connection,
                "reclaim-b",
                "reclaim-b.bundle",
                "permanent_failure",
                -2,
                0
            );
            InsertCleanupRow(
                connection,
                "reclaim-c",
                "reclaim-c.bundle",
                "permanent_failure",
                -1,
                0
            );
        }

        const long big = 300L * 1024 * 1024;
        var files = new CleanupSpyFiles(
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
        var handler = new ResponseHandler(HttpStatusCode.OK, "{}");
        var modApi = ModApiSession.TryCreate(
            "https://example.test",
            "1.0.0",
            "CleanupTest",
            TimeSpan.FromSeconds(5),
            handler
        )!;
        using var session = new BundleUploadFeed.Session(
            new TestServices(new TestPaths(root)),
            180,
            modApi,
            store,
            files
        );

        await session.RunAttemptAsync(CancellationToken.None);

        Assert(!files.Exists("uploaded.bundle"), "Uploaded files should be deleted immediately.");
        Assert(!files.Exists("old-failure.bundle"), "Seven-day permanent files should be deleted.");
        Assert(
            !files.Exists("expired-pending.bundle"),
            "Fourteen-day pending files should expire and be reclaimable."
        );
        Assert(
            !files.Exists("reclaim-a.bundle") && !files.Exists("reclaim-b.bundle"),
            "Soft-limit cleanup should reclaim oldest candidates."
        );
        Assert(
            files.Exists("reclaim-c.bundle"),
            "Soft-limit cleanup must stop once total bytes reach the threshold."
        );
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }
}

static void InsertCleanupRow(
    SqliteConnection connection,
    string bundleId,
    string fileName,
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
    command.Parameters.AddWithValue("$fileName", fileName);
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

static async Task WithSession(
    HttpStatusCode status,
    string responseBody,
    Func<UploadFixture, Task> test
)
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "bpp-upload-session-tests",
        Guid.NewGuid().ToString("N")
    );
    Directory.CreateDirectory(root);
    try
    {
        var database = Path.Combine(root, "queue.db");
        var store = new BundleQueueStore(database);
        var bytes = Encoding.UTF8.GetBytes("sealed-bundle");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO bundle_outbox (bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes, has_screenshot, sealed_at_utc, status, next_attempt_at_utc) VALUES ('bundle-a', 'run-a', 'a.bundle', $hash, 'sha-256=:digest:', $bytes, 0, $now, 'pending', $now);";
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$bytes", bytes.Length);
            command.Parameters.AddWithValue(
                "$now",
                DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o")
            );
            command.ExecuteNonQuery();
        }

        var handler = new ResponseHandler(status, responseBody);
        var modApi = ModApiSession.TryCreate(
            "https://example.test",
            "1.0.0",
            "QueueTest",
            TimeSpan.FromSeconds(5),
            handler
        )!;
        var files = new SpyFiles("a.bundle", bytes);
        var services = new TestServices(new TestPaths(root));
        var session = new BundleUploadFeed.Session(services, 180, modApi, store, files);
        await test(new UploadFixture(database, session, files, handler));
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(root, recursive: true);
    }
}

internal sealed class UploadFixture(
    string database,
    BundleUploadFeed.Session session,
    SpyFiles files,
    ResponseHandler handler
)
{
    internal BundleUploadFeed.Session Session { get; } = session;
    internal SpyFiles Files { get; } = files;
    internal ResponseHandler Handler { get; } = handler;

    internal string Status(string bundleId)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM bundle_outbox WHERE bundle_id=$bundleId;";
        command.Parameters.AddWithValue("$bundleId", bundleId);
        return (string)command.ExecuteScalar()!;
    }
}

internal sealed class SpyFiles(string fileName, byte[] bytes) : IBundleOutboxFiles
{
    private bool _exists = true;
    internal int DeleteCalls { get; private set; }
    internal Action<string>? OnDelete { get; set; }

    public Stream OpenRead(string name) =>
        _exists && name == fileName
            ? new MemoryStream(bytes, writable: false)
            : throw new FileNotFoundException();

    public bool Exists(string name) => _exists && name == fileName;

    public long GetLength(string name) =>
        Exists(name) ? bytes.Length : throw new FileNotFoundException();

    public void Delete(string name)
    {
        OnDelete?.Invoke(name);
        DeleteCalls++;
        _exists = false;
    }

    public IReadOnlyList<string> EnumerateBundleFileNames() => _exists ? [fileName] : [];
}

internal sealed class CleanupSpyFiles(Dictionary<string, long> files) : IBundleOutboxFiles
{
    public Stream OpenRead(string fileName) =>
        throw new InvalidOperationException("Cleanup should not open files.");

    public bool Exists(string fileName) => files.ContainsKey(fileName);

    public long GetLength(string fileName) => files[fileName];

    public void Delete(string fileName) => files.Remove(fileName);

    public IReadOnlyList<string> EnumerateBundleFileNames() => files.Keys.ToArray();
}

internal sealed class ResponseHandler(HttpStatusCode status, string responseBody)
    : HttpMessageHandler
{
    internal bool Disposed { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            }
        );

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

internal sealed class TestPaths(string root) : IPathProvider
{
    public string? DataRootDirectoryPath => root;
    public string? PluginsDirectoryPath => root;
}

internal sealed class TestServices(IPathProvider paths) : IBppServices
{
    public IBppEventBus EventBus { get; } = new InMemoryBppEventBus();
    public IBppConfig Config => null!;
    public IPathProvider Paths => paths;
    public IRunContext RunContext => null!;
    public IGameStateProbe GameStateProbe => null!;
    public IEncounterStateProbe EncounterState => null!;
    public IRunSnapshotProbe RunSnapshot => null!;
    public IGameBuildInfo GameBuild => new TestBuild();
}

internal sealed class TestBuild : IGameBuildInfo
{
    public string RawVersion => "test";
    public GameBuildChannel Channel => GameBuildChannel.Online;
}
