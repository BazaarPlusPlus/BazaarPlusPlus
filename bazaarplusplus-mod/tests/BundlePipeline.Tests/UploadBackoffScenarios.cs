#nullable enable
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.BundleQueue;
using Microsoft.Data.Sqlite;

/// <summary>
/// The upload retry schedule against real SQLite rows, a manual clock and a jitter-free sample:
/// an unavailable endpoint stops the batch and backs off the whole queue without reading, hashing
/// or sending anything; success, an explicit arm or a new session ends that backoff; a failure
/// about one Bundle backs off that row only.
/// </summary>
internal static class UploadBackoffScenarios
{
    private const int Base = 180;

    internal static async Task RunAsync(string root)
    {
        await UnavailableEndpointBacksOffTheWholeQueue(Path.Combine(root, "schedule"));
        await ExplicitArmEndsTheBackoff(Path.Combine(root, "arm"));
        await SuccessEndsTheBackoff(Path.Combine(root, "success"));
        await OneFailingBundleDoesNotStarveTheQueue(Path.Combine(root, "one-bad-bundle"));
        await RetryAfterFloorsTheWindowAndStaysOnTheRow(Path.Combine(root, "retry-after"));
        await BundleFailureBacksOffOnlyItsRow(Path.Combine(root, "bundle-failure"));
        await LongAttemptStillWaitsAfterItsFailure(Path.Combine(root, "long-attempt"));
    }

    // The window lines up with the attempt's start, but an attempt that outlasts it (a slow
    // request, then a timeout) still waits the base interval, or Retry-After, after its failure.
    private static async Task LongAttemptStillWaitsAfterItsFailure(string root)
    {
        int? retryAfter = null;
        using var harness = new Harness(root, ["a", "b", "c"], _ => Unavailable503(retryAfter));
        harness.RequestDuration = TimeSpan.FromSeconds(150);

        var startedAt = harness.Now;
        await harness.AttemptAsync();
        Check.That(harness.Now == startedAt.AddSeconds(150), "The clock moved during the request.");
        // The pump ticks one interval after the attempt started: the window from the attempt's
        // start is over, but only 30 s have passed since the failure.
        harness.Advance(TimeSpan.FromSeconds(Base - 150));
        harness.ExpectDeferred(await harness.AttemptAsync(), requests: 1, reads: 1);
        harness.Advance(TimeSpan.FromSeconds(150));
        await harness.AttemptAsync();
        Check.That(
            harness.Requests.Count == 2,
            "The window ends one base interval after the failure."
        );

        // The same holds for a Retry-After floor: it counts from the failure, not the start.
        harness.Publish(new UploadArmRequested());
        retryAfter = 600;
        startedAt = harness.Now;
        await harness.AttemptAsync();
        harness.Now = startedAt.AddSeconds(700);
        harness.ExpectDeferred(await harness.AttemptAsync(), requests: 3, reads: 3);
        harness.Now = startedAt.AddSeconds(750);
        await harness.AttemptAsync();
        Check.That(harness.Requests.Count == 4, "Retry-After counts from the failure.");
    }

    private static async Task UnavailableEndpointBacksOffTheWholeQueue(string root)
    {
        using var harness = new Harness(root, ["a", "b", "c"], _ => Unreachable());

        var first = await harness.AttemptAsync();
        Check.That(
            harness.Requests.SequenceEqual(["a"]) && harness.Reads == 1,
            "An unavailable endpoint must stop the batch after reading and sending one Bundle."
        );
        Check.That(
            first.Observations.Single().Kind == UploadAttemptObservationKind.Degraded,
            "The failed Bundle is reported once."
        );
        var a = harness.Row("a");
        Check.That(
            a.Status == "pending"
                && a.Attempts == 1
                && a.NextAttemptMs == harness.Now.ToUnixTimeMilliseconds()
                && a.LastErrorCode == "transport_error",
            "The failed row stays pending and due; the queue window holds the wait."
        );
        Check.That(
            harness.Row("b").Attempts == 0 && harness.Row("c").Attempts == 0,
            "The rest of the batch is not touched."
        );

        harness.Advance(TimeSpan.FromSeconds(100));
        harness.ExpectDeferred(await harness.AttemptAsync(), requests: 1, reads: 1);

        // The pump tick a few ms before the window edge still attempts.
        harness.Advance(TimeSpan.FromSeconds(Base - 100) - TimeSpan.FromMilliseconds(5));
        await harness.AttemptAsync();
        Check.That(
            harness.Requests.Count == 2 && harness.Requests[1] == "b",
            "The window ends at the base interval, and the least-attempted row goes first."
        );

        // Each further failure doubles the window up to the 30-minute cap.
        foreach (var window in new[] { 360, 720, 1440, 1800, 1800 })
            await harness.ExpectWindowAsync(window);
        Check.That(
            harness.Requests.SequenceEqual(["a", "b", "c", "a", "b", "c", "a"]),
            "One Bundle per window, rotating through the queue."
        );
    }

    private static async Task ExplicitArmEndsTheBackoff(string root)
    {
        using var harness = new Harness(root, ["a", "b", "c"], _ => Unavailable503());
        await harness.AttemptAsync();
        await harness.ExpectWindowAsync(Base);
        // Now inside a 360 s window.
        harness.Advance(TimeSpan.FromSeconds(10));
        harness.ExpectDeferred(await harness.AttemptAsync(), requests: 2, reads: 2);

        harness.Publish(new RunLifecycleChanged { IsInGameRun = true });
        harness.ExpectDeferred(await harness.AttemptAsync(), requests: 2, reads: 2);

        harness.Publish(new RunLifecycleChanged { IsInGameRun = false });
        await harness.AttemptAsync();
        Check.That(harness.Requests.Count == 3, "Leaving a run ends the backoff.");
        // The arm also reset the failure count: the next window is the base again.
        await harness.ExpectWindowAsync(Base);

        harness.Advance(TimeSpan.FromSeconds(10));
        harness.ExpectDeferred(await harness.AttemptAsync(), requests: 4, reads: 4);
        harness.Publish(new UploadArmRequested());
        await harness.AttemptAsync();
        Check.That(harness.Requests.Count == 5, "UploadArmRequested ends the backoff.");
    }

    private static async Task SuccessEndsTheBackoff(string root)
    {
        using var harness = new Harness(
            root,
            ["a", "b", "c", "d"],
            bundleId => bundleId == "c" ? Accepted(bundleId) : Unavailable503()
        );
        await harness.AttemptAsync();
        await harness.ExpectWindowAsync(Base);
        // At the 360 s window's end the batch is c (accepted), then d (unavailable): the success
        // reset the count, so d opens a base window, not 720 s.
        await harness.ExpectWindowAsync(360);
        Check.That(
            harness.Requests.SequenceEqual(["a", "b", "c", "d"]),
            "The batch continues past a success."
        );
        Check.That(harness.Row("c").Status == "uploaded", "The accepted Bundle is committed.");
        await harness.ExpectWindowAsync(Base);
    }

    // Resilience: a 5xx is service-wide by the API reference, but if one Bundle keeps getting one
    // while the others are accepted, the rest of the queue must still drain.
    private static async Task OneFailingBundleDoesNotStarveTheQueue(string root)
    {
        using var harness = new Harness(
            root,
            ["a", "b", "c", "d", "e"],
            bundleId => bundleId == "a" ? Unavailable503() : Accepted(bundleId)
        );
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await harness.AttemptAsync();
            harness.Advance(TimeSpan.FromSeconds(Base));
        }
        foreach (var bundleId in new[] { "b", "c", "d", "e" })
            Check.That(
                harness.Row(bundleId).Status == "uploaded",
                $"Bundle {bundleId} must upload while another Bundle keeps failing."
            );
        Check.That(harness.Row("a").Status == "pending", "The failing Bundle stays pending.");
    }

    private static async Task RetryAfterFloorsTheWindowAndStaysOnTheRow(string root)
    {
        var retryAfter = 600;
        using var harness = new Harness(root, ["a", "b"], _ => Unavailable503(retryAfter));
        var openedAt = harness.Now;
        await harness.AttemptAsync();
        Check.That(
            harness.Row("a").NextAttemptMs == (openedAt.AddSeconds(600)).ToUnixTimeMilliseconds(),
            "The failed row carries the server's Retry-After."
        );
        harness.Advance(TimeSpan.FromSeconds(500));
        harness.ExpectDeferred(await harness.AttemptAsync(), requests: 1, reads: 1);
        harness.Advance(TimeSpan.FromSeconds(100));
        retryAfter = 7200;
        openedAt = harness.Now;
        await harness.AttemptAsync();
        Check.That(
            harness.Requests.SequenceEqual(["a", "b"]),
            "The Retry-After window ends on time."
        );
        Check.That(
            harness.Row("b").NextAttemptMs == openedAt.AddMinutes(30).ToUnixTimeMilliseconds(),
            "A Retry-After beyond the cap is clamped to 30 minutes on the row."
        );
        harness.Advance(TimeSpan.FromSeconds(1700));
        harness.ExpectDeferred(await harness.AttemptAsync(), requests: 2, reads: 2);
        harness.Advance(TimeSpan.FromSeconds(100));
        await harness.AttemptAsync();
        Check.That(harness.Requests.Count == 3, "The queue window is clamped to 30 minutes.");
    }

    private static async Task BundleFailureBacksOffOnlyItsRow(string root)
    {
        using var harness = new Harness(root, ["a", "b", "c"], _ => WrongReceipt());
        var first = harness.Now;
        await harness.AttemptAsync();
        Check.That(
            harness.Requests.SequenceEqual(["a", "b", "c"]),
            "A failure about one Bundle does not stop the batch."
        );
        foreach (var bundleId in new[] { "a", "b", "c" })
        {
            var row = harness.Row(bundleId);
            Check.That(
                row.Attempts == 1
                    && row.LastErrorCode == "invalid_success_receipt"
                    && row.NextAttemptMs == first.AddSeconds(Base).ToUnixTimeMilliseconds(),
                $"Bundle {bundleId} backs off by the base interval after its first failure."
            );
        }

        harness.Advance(TimeSpan.FromSeconds(1));
        var idle = await harness.AttemptAsync();
        Check.That(
            idle.Observations.Single().Kind == UploadAttemptObservationKind.NoWork,
            "Per-row backoff leaves no queue backoff: the next attempt just finds nothing due."
        );

        harness.Advance(TimeSpan.FromSeconds(Base - 1));
        var second = harness.Now;
        await harness.AttemptAsync();
        Check.That(
            harness.Requests.Count == 6
                && harness.Row("a").NextAttemptMs
                    == second.AddSeconds(2 * Base).ToUnixTimeMilliseconds(),
            "The row delay doubles with its recorded attempts."
        );
    }

    private static object Unreachable() => new HttpRequestException("network is unreachable");

    private static object Unavailable503(int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        if (retryAfterSeconds is { } seconds)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                TimeSpan.FromSeconds(seconds)
            );
        return response;
    }

    private static object Accepted(string bundleId) =>
        new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                $"{{\"bundle_id\":\"{bundleId}\",\"run_id\":\"run-{bundleId}\",\"outcome\":\"stored\"}}",
                Encoding.UTF8,
                "application/json"
            ),
        };

    private static object WrongReceipt() =>
        new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                "{\"bundle_id\":\"other\",\"run_id\":\"other\",\"outcome\":\"stored\"}",
                Encoding.UTF8,
                "application/json"
            ),
        };

    internal readonly record struct OutboxRow(
        string Status,
        int Attempts,
        long NextAttemptMs,
        string? LastErrorCode
    );

    /// <summary>
    /// One session over its own database, Bundle files held in memory (their bytes are the Bundle
    /// id, so the handler knows which one it got), a manual clock and a jitter sample of 0.5.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        private readonly string _database;
        private readonly CountingFiles _files;
        private readonly ScriptedHandler _handler;
        private readonly TestServices _services;
        private readonly BundleUploadFeed.Session _session;
        private readonly IDisposable? _armSubscription;

        internal Harness(string root, string[] bundleIds, Func<string, object> answer)
        {
            Directory.CreateDirectory(root);
            _database = Path.Combine(root, "queue.db");
            Now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var store = new BundleQueueStore(_database);
            var files = new Dictionary<string, byte[]>();
            using (var connection = new SqliteConnection($"Data Source={_database}"))
            {
                connection.Open();
                for (var index = 0; index < bundleIds.Length; index++)
                {
                    var bytes = Encoding.UTF8.GetBytes(bundleIds[index]);
                    files[bundleIds[index] + ".bundle"] = bytes;
                    using var command = connection.CreateCommand();
                    command.CommandText =
                        "INSERT INTO bundle_outbox (bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes, has_screenshot, sealed_at_ms, status, next_attempt_at_ms) VALUES ($bundleId, $runId, $fileName, $sha, 'digest', $length, 0, $sealedAt, 'pending', $sealedAt);";
                    command.Parameters.AddWithValue("$bundleId", bundleIds[index]);
                    command.Parameters.AddWithValue("$runId", "run-" + bundleIds[index]);
                    command.Parameters.AddWithValue("$fileName", bundleIds[index] + ".bundle");
                    command.Parameters.AddWithValue(
                        "$sha",
                        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                    );
                    command.Parameters.AddWithValue("$length", bytes.Length);
                    command.Parameters.AddWithValue(
                        "$sealedAt",
                        Now.AddMinutes(index - bundleIds.Length).ToUnixTimeMilliseconds()
                    );
                    command.ExecuteNonQuery();
                }
            }
            _files = new CountingFiles(files);
            _handler = new ScriptedHandler(bundleId =>
            {
                Now += RequestDuration;
                return answer(bundleId);
            });
            _services = new TestServices(new TestPaths(root));
            _session = new BundleUploadFeed.Session(
                _services,
                Base,
                ModApiSession.TryCreate(
                    "https://example.test",
                    "test",
                    platformKey: null,
                    "BackoffTest",
                    TimeSpan.FromSeconds(5),
                    _handler
                ) ?? throw new InvalidOperationException("test Mod API session"),
                store,
                _files,
                () => Now,
                () => 0.5
            );
            _armSubscription = _session.SubscribeArmSignals();
        }

        internal DateTimeOffset Now { get; set; }

        // How far the clock moves while a request is in flight; zero unless a scenario sets it.
        internal TimeSpan RequestDuration { get; set; }
        internal IReadOnlyList<string> Requests => _handler.Requests;
        internal int Reads => _files.Reads;

        internal void Advance(TimeSpan by) => Now += by;

        internal void Publish<TEvent>(TEvent eventData)
            where TEvent : class => _services.EventBus.Publish(eventData);

        internal Task<UploadAttemptResult> AttemptAsync() =>
            _session.RunAttemptAsync(CancellationToken.None);

        internal void ExpectDeferred(UploadAttemptResult result, int requests, int reads)
        {
            var observation = result.Observations.Single();
            Check.That(
                observation.Kind == UploadAttemptObservationKind.Deferred
                    && observation.ReasonCode == UploadLogReasonCode.QueueBackoff,
                "A backing-off session defers the attempt."
            );
            Check.That(
                Requests.Count == requests && Reads == reads,
                "A backing-off session reads, hashes and sends nothing."
            );
        }

        // The current window is `seconds` long: deferred a minute before its end, attempted at it.
        internal async Task ExpectWindowAsync(int seconds)
        {
            var requests = Requests.Count;
            var reads = Reads;
            var openedAt = Now;
            Now = openedAt.AddSeconds(seconds - 60);
            ExpectDeferred(await AttemptAsync(), requests, reads);
            Now = openedAt.AddSeconds(seconds);
            await AttemptAsync();
            Check.That(Requests.Count > requests, $"The {seconds} s window must end on time.");
        }

        internal OutboxRow Row(string bundleId)
        {
            using var connection = new SqliteConnection($"Data Source={_database}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT status, attempts, next_attempt_at_ms, last_error_code FROM bundle_outbox WHERE bundle_id = $bundleId;";
            command.Parameters.AddWithValue("$bundleId", bundleId);
            using var reader = command.ExecuteReader();
            Check.That(reader.Read(), $"Bundle {bundleId} has no outbox row.");
            return new OutboxRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            );
        }

        public void Dispose()
        {
            _armSubscription?.Dispose();
            _session.Dispose();
        }
    }

    private sealed class ScriptedHandler(Func<string, object> answer) : HttpMessageHandler
    {
        internal List<string> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var bundleId = Encoding.UTF8.GetString(
                await request.Content!.ReadAsByteArrayAsync(cancellationToken)
            );
            Requests.Add(bundleId);
            return answer(bundleId) switch
            {
                HttpResponseMessage response => response,
                Exception exception => throw exception,
                var other => throw new InvalidOperationException($"Unexpected answer {other}."),
            };
        }
    }

    private sealed class CountingFiles(Dictionary<string, byte[]> files) : IBundleOutboxFiles
    {
        internal int Reads { get; private set; }

        public Stream OpenRead(string fileName)
        {
            Reads++;
            return new MemoryStream(files[fileName], writable: false);
        }

        public bool Exists(string fileName) => files.ContainsKey(fileName);

        public long GetLength(string fileName) => files[fileName].Length;

        public void Delete(string fileName) => files.Remove(fileName);

        public IReadOnlyList<string> EnumerateBundleFileNames() => files.Keys.ToArray();
    }
}
