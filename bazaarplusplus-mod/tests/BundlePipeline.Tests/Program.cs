using System.Net;
using System.Text;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.TestSupport;
using MessagePack;
using Microsoft.Data.Sqlite;

var root = Path.Combine(Path.GetTempPath(), "bpp-v5-pipeline-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var paths = new TestPaths(root);
    var store = new RunLogStore(paths);
    var database = PathConstants.RunLogDatabase(root);
    var started = DateTimeOffset.UtcNow.AddMinutes(-3);
    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-seal-001",
            StartedAtUtc = started,
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = "account-001",
            BundleScreenshotRequested = false,
            ModVersion = "5.0.0",
        }
    );
    new PvpBattleSqliteStore(database).Save(
        new PvpBattleManifest
        {
            BattleId = "battle-seal-001",
            RunId = "run-seal-001",
            RecordedAtUtc = started.AddSeconds(30),
            CombatKind = "PVPCombat",
            Day = 1,
            Hour = 1,
            Participants = new PvpBattleParticipants
            {
                PlayerName = "Player",
                PlayerAccountId = "account-001",
                PlayerHero = "Vanessa",
                OpponentName = "Opponent",
                OpponentAccountId = "account-002",
                OpponentHero = "Pygmalien",
            },
            Outcome = new PvpBattleOutcome { Result = "Win" },
            Snapshots = new PvpBattleSnapshots
            {
                PlayerHand = CapturedEmpty(),
                PlayerSkills = CapturedEmpty(),
                OpponentHand = CapturedEmpty(),
                OpponentSkills = CapturedEmpty(),
            },
        }
    );
    new CombatReplayPayloadStore(PathConstants.CombatReplays(root)).Save(
        new PvpReplayPayload
        {
            BattleId = "battle-seal-001",
            SpawnMessageBytes = MessagePackSerializer.Serialize(
                new NetMessageGameSim(new GameSim()),
                MessagePackConfig.Options
            ),
            CombatMessageBytes = MessagePackSerializer.Serialize(
                new NetMessageCombatSim(new CombatSim()),
                MessagePackConfig.Options
            ),
            DespawnMessageBytes = MessagePackSerializer.Serialize(
                new NetMessageGameSim(new GameSim()),
                MessagePackConfig.Options
            ),
        }
    );
    store.CompleteRun(
        "run-seal-001",
        new RunLogCompletion { EndedAtUtc = started.AddMinutes(1), Status = "completed" }
    );

    var services = new TestServices(paths);
    using var coordinator = new BundleSealCoordinator(services);
    await coordinator.ReconcileAsync(CancellationToken.None);

    using (var connection = new SqliteConnection($"Data Source={database}"))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT bundle_id, file_name, status FROM bundle_outbox WHERE run_id='run-seal-001';";
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            reader.Close();
            using var job = connection.CreateCommand();
            job.CommandText =
                "SELECT state, last_error_code, last_error_detail FROM bundle_seal_jobs WHERE run_id='run-seal-001';";
            using var jobReader = job.ExecuteReader();
            var detail = jobReader.Read()
                ? $"{jobReader.GetString(0)}/{(jobReader.IsDBNull(1) ? "" : jobReader.GetString(1))}/{(jobReader.IsDBNull(2) ? "" : jobReader.GetString(2))}"
                : "job missing";
            throw new InvalidOperationException("completed run should seal into outbox: " + detail);
        }
        var bundleId = reader.GetString(0);
        var file = Path.Combine(PathConstants.BundleOutbox(root), reader.GetString(1));
        Assert(reader.GetString(2) == "pending", "new outbox row should be pending");
        Assert(File.Exists(file), "sealed bundle file should exist");
        var opened = BundleV5Codec.Open(TestInputs.ScratchBytes(file));
        Assert(opened.Manifest.BundleId == bundleId, "file identity should match outbox");
        Assert(opened.Manifest.Screenshot == null, "screenshot-disabled run should be Run-only");
        var payload = RunPayloadV5Codec.Decode(opened.RunPayload);
        Assert(payload.RunId == "run-seal-001", "payload should preserve run identity");
        Assert(payload.PlayerAccountId == "account-001", "payload should preserve frozen account");
        Assert(
            payload.ReplayableBattleIds.SequenceEqual(new[] { "battle-seal-001" }),
            "composer should use the shared replayability contract"
        );
        var runBundle = RunBundleV5Contract.Open(TestInputs.ScratchBytes(file));
        Assert(runBundle.Succeeded, "sealed bundle should satisfy the Run Bundle contract");
        Assert(
            runBundle.Value!.TryGetReplayableBattle("battle-seal-001", out _),
            "sealed replayable battle should resolve through the shared contract"
        );
    }

    using (var repository = new SqliteConnection($"Data Source={database}"))
    {
        repository.Open();
        using var delete = repository.CreateCommand();
        delete.CommandText = "DELETE FROM runs WHERE run_id='run-seal-001';";
        delete.ExecuteNonQuery();
        using var count = repository.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM bundle_outbox WHERE run_id='run-seal-001';";
        Assert(
            Convert.ToInt32(count.ExecuteScalar()) == 1,
            "outbox must not FK-block Run deletion"
        );
    }

    var waitingStarted = DateTimeOffset.UtcNow.AddMinutes(-1);
    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-waits-for-screenshot",
            StartedAtUtc = waitingStarted,
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = "account-001",
            BundleScreenshotRequested = true,
            ModVersion = "5.0.0",
        }
    );
    store.CompleteRun(
        "run-waits-for-screenshot",
        new RunLogCompletion { EndedAtUtc = DateTimeOffset.UtcNow, Status = "completed" }
    );
    await coordinator.ReconcileAsync(CancellationToken.None);
    using (var connection = new SqliteConnection($"Data Source={database}"))
    {
        connection.Open();
        using var pending = connection.CreateCommand();
        pending.CommandText =
            "SELECT COUNT(*) FROM bundle_outbox WHERE run_id='run-waits-for-screenshot';";
        Assert(
            Convert.ToInt32(pending.ExecuteScalar()) == 0,
            "a requested screenshot should keep the job waiting before its UTC deadline"
        );
        using var expire = connection.CreateCommand();
        expire.CommandText =
            "UPDATE bundle_seal_jobs SET input_deadline_at_utc=datetime('now','-1 second') WHERE run_id='run-waits-for-screenshot';";
        expire.ExecuteNonQuery();
    }
    await coordinator.ReconcileAsync(CancellationToken.None);
    using (var connection = new SqliteConnection($"Data Source={database}"))
    {
        connection.Open();
        using var sealedAfterDeadline = connection.CreateCommand();
        sealedAfterDeadline.CommandText =
            "SELECT has_screenshot FROM bundle_outbox WHERE run_id='run-waits-for-screenshot' AND status='pending';";
        Assert(
            Convert.ToInt32(sealedAfterDeadline.ExecuteScalar()) == 0,
            "the same job should seal Run-only once its UTC deadline expires"
        );
    }

    await VerifyRecoveryAsync(root, store, coordinator, database);
    await VerifyUploadClientAsync();
    Console.WriteLine("Bundle pipeline tests passed.");
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(root, recursive: true);
}

static PvpBattleCardSetCapture CapturedEmpty() =>
    new() { Status = PvpBattleCaptureStatus.CapturedEmpty };

static async Task VerifyRecoveryAsync(
    string root,
    RunLogStore store,
    BundleSealCoordinator coordinator,
    string database
)
{
    var outboxRoot = PathConstants.BundleOutbox(root);
    Directory.CreateDirectory(outboxRoot);

    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-orphan-adopt",
            StartedAtUtc = DateTimeOffset.UtcNow,
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = "account-adopt",
        }
    );
    var adopted = BuildRunOnlyBundle(
        "01J00000000000000000000AD0",
        "run-orphan-adopt",
        "account-adopt",
        1000
    );
    InsertSealJob(database, "run-orphan-adopt", adopted.Manifest.BundleId, 1000, "account-adopt");
    File.WriteAllBytes(
        Path.Combine(outboxRoot, adopted.Manifest.BundleId + ".bundle"),
        adopted.Bytes
    );

    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-orphan-mismatch",
            StartedAtUtc = DateTimeOffset.UtcNow,
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = "account-mismatch",
        }
    );
    var mismatch = BuildRunOnlyBundle(
        "01J00000000000000000000BAD",
        "run-orphan-mismatch",
        "account-mismatch",
        2000
    );
    InsertSealJob(
        database,
        "run-orphan-mismatch",
        "01J00000000000000000000EXP",
        2000,
        "account-mismatch"
    );
    var mismatchPath = Path.Combine(outboxRoot, mismatch.Manifest.BundleId + ".bundle");
    File.WriteAllBytes(mismatchPath, mismatch.Bytes);
    File.SetLastWriteTimeUtc(mismatchPath, DateTime.UtcNow.AddHours(-25));

    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-invalid-pending",
            StartedAtUtc = DateTimeOffset.UtcNow,
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = null,
        }
    );
    using (var connection = new SqliteConnection($"Data Source={database}"))
    {
        connection.Open();
        using var invalid = connection.CreateCommand();
        invalid.CommandText =
            "INSERT INTO bundle_outbox (bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes, has_screenshot, sealed_at_utc, status, next_attempt_at_utc) VALUES ('invalid-bundle', 'run-invalid-pending', 'invalid.bundle', 'sha', 'digest', 3, 0, $now, 'pending', $now);";
        invalid.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("o"));
        invalid.ExecuteNonQuery();
    }
    File.WriteAllText(Path.Combine(outboxRoot, "invalid.bundle"), "bad");

    await coordinator.ReconcileAsync(CancellationToken.None);

    using var verify = new SqliteConnection($"Data Source={database}");
    verify.Open();
    Assert(
        Scalar(
            verify,
            "SELECT status FROM bundle_outbox WHERE bundle_id='" + adopted.Manifest.BundleId + "';"
        ) == "pending",
        "A matching allocated orphan should be adopted atomically."
    );
    Assert(!File.Exists(mismatchPath), "An expired allocation-mismatch orphan should be deleted.");
    Assert(
        Scalar(verify, "SELECT status FROM bundle_outbox WHERE bundle_id='invalid-bundle';")
            == "permanent_failure",
        "An invalid pending sidecar should fail its outbox row."
    );
    Assert(
        Convert.ToInt32(
            ScalarObject(
                verify,
                "SELECT COUNT(*) FROM bundle_seal_jobs WHERE run_id='run-invalid-pending';"
            )
        ) == 1,
        "Invalid pending recovery should create a durable reseal job before processing it."
    );
}

static BundleBuildResultV5 BuildRunOnlyBundle(
    string bundleId,
    string runId,
    string accountId,
    long createdAtMs
) =>
    BundleV5Codec.Build(
        new BundleBuildInputV5
        {
            BundleId = bundleId,
            CreatedAtMs = createdAtMs,
            RunId = runId,
            PlayerAccountId = accountId,
            RunPayload = RunPayloadV5Codec.Encode(
                new RunPayloadV5 { RunId = runId, PlayerAccountId = accountId }
            ),
        }
    );

static void InsertSealJob(
    string database,
    string runId,
    string bundleId,
    long createdAtMs,
    string accountId
)
{
    using var connection = new SqliteConnection($"Data Source={database}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
        "INSERT INTO bundle_seal_jobs (run_id, state, player_account_id, screenshot_requested, screenshot_state, input_deadline_at_utc, bundle_id, created_at_ms) VALUES ($runId, 'sealing', $accountId, 0, 'not_requested', $now, $bundleId, $createdAtMs);";
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$accountId", accountId);
    command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.AddMinutes(2).ToString("o"));
    command.Parameters.AddWithValue("$bundleId", bundleId);
    command.Parameters.AddWithValue("$createdAtMs", createdAtMs);
    command.ExecuteNonQuery();
}

static string? Scalar(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return command.ExecuteScalar() as string;
}

static object? ScalarObject(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return command.ExecuteScalar();
}

static async Task VerifyUploadClientAsync()
{
    var payload = RunPayloadV5Codec.Encode(
        new RunPayloadV5 { RunId = "run-http", PlayerAccountId = "account-http" }
    );
    var built = BundleV5Codec.Build(
        new BundleBuildInputV5
        {
            BundleId = "01J00000000000000000000111",
            CreatedAtMs = 1_785_628_800_000,
            RunId = "run-http",
            PlayerAccountId = "account-http",
            RunPayload = payload,
        }
    );
    var responses = new Queue<HttpResponseMessage>(
        new[]
        {
            Json(
                HttpStatusCode.Created,
                "{\"bundle_id\":\"01J00000000000000000000111\",\"run_id\":\"run-http\",\"outcome\":\"stored\",\"bazaardb_delivery\":\"not_applicable\"}"
            ),
            Json(
                HttpStatusCode.OK,
                "{\"bundle_id\":\"01J00000000000000000000111\",\"run_id\":\"run-http\",\"outcome\":\"duplicate\",\"bazaardb_delivery\":\"not_applicable\"}"
            ),
            Json(
                HttpStatusCode.Conflict,
                "{\"error\":{\"code\":\"bundle_id_conflict\",\"message\":\"different\",\"retryable\":false,\"request_id\":\"body-request-id\"}}"
            ),
            Json(HttpStatusCode.Conflict, "{\"error\":\"bundle_id_conflict\",\"retryable\":false}"),
            Json(
                HttpStatusCode.ServiceUnavailable,
                "{\"error\":{\"code\":\"storage_unavailable\",\"message\":\"later\",\"retryable\":true,\"request_id\":\"r\"}}"
            ),
        }
    );
    var handler = new CaptureHandler(responses);
    using var session =
        ModApiSession.TryCreate(
            "https://example.test",
            "test",
            "BundleUpload",
            TimeSpan.FromSeconds(120),
            handler
        ) ?? throw new InvalidOperationException("test Mod API session");
    var dispositions = new List<BundleUploadDisposition>();
    var results = new List<BundleUploadResponse>();
    for (var index = 0; index < 5; index++)
    {
        using var stream = new MemoryStream(built.Bytes, writable: false);
        var response = await session.UploadBundleAsync(
            stream,
            built.Bytes.Length,
            built.ContentDigest,
            built.Manifest.BundleId,
            built.Manifest.Run.RunId,
            CancellationToken.None
        );
        results.Add(response);
        dispositions.Add(response.Disposition);
    }
    Assert(
        dispositions.SequenceEqual(
            new[]
            {
                BundleUploadDisposition.Uploaded,
                BundleUploadDisposition.Uploaded,
                BundleUploadDisposition.Permanent,
                BundleUploadDisposition.Transient,
                BundleUploadDisposition.Transient,
            }
        ),
        "upload response matrix should classify exactly"
    );
    Assert(
        results[2].RequestId == "body-request-id",
        "nested body request ID should remain available when the header is absent"
    );
    Assert(
        handler.Bodies.All(body => body.SequenceEqual(built.Bytes)),
        "retries must be byte-identical"
    );
    Assert(
        handler.ContentDigests.All(value => value == built.ContentDigest),
        "Content-Digest must be exact"
    );
}

static HttpResponseMessage Json(HttpStatusCode status, string body) =>
    new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class CaptureHandler(Queue<HttpResponseMessage> responses) : HttpMessageHandler
{
    internal List<byte[]> Bodies { get; } = new();
    internal List<string?> ContentDigests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Bodies.Add(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
        ContentDigests.Add(request.Headers.GetValues("Content-Digest").Single());
        if (request.Content.Headers.ContentLength != Bodies[^1].Length)
            throw new InvalidOperationException("Content-Length required");
        if (request.Content.Headers.ContentType!.MediaType != BundleLimitsV5.BundleContentType)
            throw new InvalidOperationException("Bundle Content-Type required");
        return responses.Dequeue();
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
