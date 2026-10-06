using System.Net;
using System.Text;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.Upload;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunScreenshot;
using BazaarPlusPlus.TestSupport;
using BepInEx.Logging;
using MessagePack;
using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// The Bundle pipeline anchor: real SQLite run and battle stores -> replay payloads ->
// BundleSealCoordinator -> BundleQueueStore outbox -> BundleUploadFeed.Session -> ModApiSession.
// Every sealed Bundle lands in artifacts/bundle-pipeline/ and the normalized summary must match
// fixtures/bundle-pipeline.golden.json (PipelineArtifacts owns the format and regeneration).
var root = Path.Combine(Path.GetTempPath(), "bpp-v5-pipeline-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    // Privacy anchor: the real log stack observes the whole seal -> outbox -> upload run.
    var logLines = new List<string>();
    var logSource = new ManualLogSource("bundle-pipeline-privacy");
    logSource.LogEvent += (_, args) => logLines.Add(args.Data?.ToString() ?? string.Empty);
    BppLog.Install(logSource);

    var artifacts = new PipelineArtifacts();
    var dataRoot = Path.Combine(root, "pipeline");
    Directory.CreateDirectory(dataRoot);
    var paths = new TestPaths(dataRoot);
    var store = new RunLogStore(paths);
    var database = PathConstants.RunLogDatabase(dataRoot);
    var replays = new CombatReplayPayloadStore(PathConstants.CombatReplays(dataRoot));
    using var coordinator = new BundleSealCoordinator(new TestServices(paths));

    RoutingKeepsPveOutOfPersistence(database);

    // run-only: one replayable PvP battle, no screenshot requested.
    CreateRun(store, "run-seal-001", minute: 0, screenshotRequested: false);
    SaveBattle(database, "battle-seal-001", "run-seal-001", minute: 0);
    SaveReplay(replays, "battle-seal-001");
    var roundTrip = replays.LoadDetailed("battle-seal-001");
    Check.That(
        roundTrip.Status == FileBackedPayloadLoadStatus.Loaded
            && roundTrip.Payload!.CombatMessageBytes.SequenceEqual(ReplayBytes.Combat),
        "Replay payload bytes must round-trip exactly."
    );
    CompleteRun(store, "run-seal-001", minute: 0);
    await coordinator.ReconcileAsync(CancellationToken.None);
    var runOnly = Sealed(artifacts, dataRoot, "run-only", "run-seal-001");
    Check.That(runOnly.Opened.Manifest.Screenshot == null, "No screenshot was requested.");
    Check.That(runOnly.Payload.PlayerAccountId == "account-001", "Frozen account is kept.");
    Check.That(
        runOnly.Payload.ReplayableBattleIds.SequenceEqual(new[] { "battle-seal-001" }),
        "The composer should use the shared replayability contract."
    );
    Check.That(
        RunBundleV5Contract
            .Open(runOnly.Bytes)
            .Value!.TryGetReplayableBattle("battle-seal-001", out _),
        "The sealed replayable battle should resolve through the shared contract."
    );
    artifacts.RecordQueue("run-only", database, "run-seal-001");
    Execute(database, "DELETE FROM runs WHERE run_id='run-seal-001';");
    Check.That(
        Count(database, "bundle_outbox", "run-seal-001") == 1,
        "The outbox must not FK-block Run deletion."
    );

    // screenshot: a requested end-of-run screenshot is present when the job seals.
    CreateRun(store, "run-screenshot", minute: 60, screenshotRequested: true);
    var capturedAtUtc = PipelineInputs.Base.AddMinutes(61).AddSeconds(50);
    var relativeImage = "2099-01-01/2099-01-01_01-01-50-000_final_run-run-screenshot.png";
    WritePng(Path.Combine(PathConstants.Screenshots(dataRoot), relativeImage));
    var screenshots = new RunScreenshotSqliteStore(database);
    screenshots.Save(Screenshot("shot-primary-001", relativeImage, capturedAtUtc));
    ExpectSqliteConstraint(
        () =>
            screenshots.Save(
                Screenshot("shot-primary-002", "2099-01-01/duplicate.png", capturedAtUtc)
            ),
        "Only one primary screenshot may exist per run."
    );
    Check.That(
        screenshots.TryGetLatestPrimaryForRun("missing-run") == null,
        "A run without a primary screenshot has none."
    );
    CompleteRun(store, "run-screenshot", minute: 60);
    await coordinator.ReconcileAsync(CancellationToken.None);
    var shot = Sealed(artifacts, dataRoot, "screenshot", "run-screenshot");
    Check.That(
        Number(database, "SELECT has_screenshot FROM bundle_outbox WHERE run_id='run-screenshot';")
            == 1,
        "A sealed screenshot sets has_screenshot."
    );
    Check.That(
        shot.Opened.Manifest.Screenshot?.CapturedAtMs == capturedAtUtc.ToUnixTimeMilliseconds()
            && shot.Opened.Screenshot is { Length: > 0 }
            && !shot.Payload.Degradation.ScreenshotOmitted,
        "The primary screenshot should seal into the Bundle."
    );
    artifacts.RecordScreenshots(database, "run-screenshot");
    artifacts.RecordQueue("screenshot", database, "run-screenshot");

    // screenshot-deadline: a requested screenshot that never arrives waits, then seals Run-only.
    CreateRun(store, "run-waits-for-screenshot", minute: 120, screenshotRequested: true);
    CompleteRun(store, "run-waits-for-screenshot", minute: 120);
    await coordinator.ReconcileAsync(CancellationToken.None);
    Check.That(
        Count(database, "bundle_outbox", "run-waits-for-screenshot") == 0,
        "A requested screenshot keeps the job waiting before its deadline."
    );
    artifacts.RecordQueue("screenshot-deadline:waiting", database, "run-waits-for-screenshot");
    ExpireDeadline(database, "run-waits-for-screenshot");
    await coordinator.ReconcileAsync(CancellationToken.None);
    var timedOut = Sealed(artifacts, dataRoot, "screenshot-deadline", "run-waits-for-screenshot");
    Check.That(
        timedOut.Opened.Manifest.Screenshot == null
            && timedOut.Payload.Degradation.ScreenshotOmitted,
        "The same job seals Run-only once its deadline expires."
    );
    artifacts.RecordQueue("screenshot-deadline:sealed", database, "run-waits-for-screenshot");

    // replay-persistence: a Run whose replay writes are still in flight waits; others seal.
    CreateRun(store, "run-replay-held", minute: 180, screenshotRequested: false);
    CreateRun(store, "run-replay-free", minute: 181, screenshotRequested: false);
    ReplayPersistenceStateTracker.Enqueued("run-replay-held");
    CompleteRun(store, "run-replay-held", minute: 180);
    CompleteRun(store, "run-replay-free", minute: 181);
    await coordinator.ReconcileAsync(CancellationToken.None);
    Check.That(
        Count(database, "bundle_outbox", "run-replay-held") == 0,
        "A Run with pending replay persistence must not seal."
    );
    Sealed(artifacts, dataRoot, "replay-free", "run-replay-free");
    artifacts.RecordQueue(
        "replay-persistence:held",
        database,
        "run-replay-held",
        "run-replay-free"
    );
    ReplayPersistenceStateTracker.Completed("run-replay-held");
    await coordinator.ReconcileAsync(CancellationToken.None);
    Sealed(artifacts, dataRoot, "replay-held", "run-replay-held");
    artifacts.RecordQueue("replay-persistence:drained", database, "run-replay-held");

    // corrupt-replay: an undecodable replay waits for the deadline, then is omitted.
    CreateRun(store, "run-corrupt-replay", minute: 240, screenshotRequested: false);
    SaveBattle(database, "battle-corrupt", "run-corrupt-replay", minute: 240);
    SaveReplay(replays, "battle-corrupt");
    File.WriteAllBytes(
        Directory.EnumerateFiles(PathConstants.CombatReplays(dataRoot), "battle-corrupt*").Single(),
        [0, 1, 2, 3]
    );
    Check.That(
        replays.LoadDetailed("battle-corrupt").Status == FileBackedPayloadLoadStatus.Invalid,
        "A corrupt replay file is classified as invalid."
    );
    CompleteRun(store, "run-corrupt-replay", minute: 240);
    await coordinator.ReconcileAsync(CancellationToken.None);
    Check.That(
        Count(database, "bundle_outbox", "run-corrupt-replay") == 0,
        "An omitted replay keeps the job waiting before its deadline."
    );
    artifacts.RecordQueue("corrupt-replay:waiting", database, "run-corrupt-replay");
    ExpireDeadline(database, "run-corrupt-replay");
    await coordinator.ReconcileAsync(CancellationToken.None);
    var corrupt = Sealed(artifacts, dataRoot, "corrupt-replay", "run-corrupt-replay");
    Check.That(
        corrupt.Payload.Degradation.ReplayOmittedBattleIds.SequenceEqual(new[] { "battle-corrupt" })
            && !corrupt.Payload.ReplayableBattleIds.Contains("battle-corrupt"),
        "The corrupt replay is omitted explicitly at seal."
    );
    artifacts.RecordQueue("corrupt-replay:sealed", database, "run-corrupt-replay");

    await UploadStage.RunAsync(
        dataRoot,
        artifacts,
        [
            "run-seal-001",
            "run-screenshot",
            "run-waits-for-screenshot",
            "run-replay-free",
            "run-replay-held",
            "run-corrupt-replay",
        ]
    );
    BppLog.Flush();
    LogPrivacy.Verify(logLines);

    // The golden comes first so a pipeline regression shows as an artifact diff.
    if (!artifacts.CompareWithGolden())
        return 1;

    await BundleSealFailureTests.RunAsync(Path.Combine(root, "seal-failures"));
    await UploadFeedBoundaries.RunAsync(Path.Combine(root, "upload-boundaries"));
    await VerifyRecoveryAsync(Path.Combine(root, "recovery"));
    await VerifyLegacyJsonRecoveryAsync(Path.Combine(root, "legacy-json"));
    await VerifyUploadClientAsync();
}
finally
{
    SqliteConnection.ClearAllPools();
    Directory.Delete(root, recursive: true);
}
Console.WriteLine("Bundle pipeline tests passed.");
return 0;

static void CreateRun(RunLogStore store, string runId, int minute, bool screenshotRequested) =>
    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = runId,
            StartedAtUtc = PipelineInputs.Base.AddMinutes(minute),
            Hero = "Vanessa",
            GameMode = "Ranked",
            PlayerAccountId = "account-001",
            BundleScreenshotRequested = screenshotRequested,
            ModVersion = "5.0.0",
        }
    );

static void CompleteRun(RunLogStore store, string runId, int minute) =>
    store.CompleteRun(
        runId,
        new RunLogCompletion
        {
            EndedAtUtc = PipelineInputs.Base.AddMinutes(minute + 1),
            Status = "completed",
        }
    );

static void SaveBattle(string database, string battleId, string runId, int minute) =>
    new PvpBattleSqliteStore(database).Save(
        new PvpBattleManifest
        {
            BattleId = battleId,
            RunId = runId,
            RecordedAtUtc = PipelineInputs.Base.AddMinutes(minute).AddSeconds(30),
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

static void SaveReplay(CombatReplayPayloadStore replays, string battleId) =>
    replays.Save(
        new PvpReplayPayload
        {
            BattleId = battleId,
            SpawnMessageBytes = ReplayBytes.Spawn,
            CombatMessageBytes = ReplayBytes.Combat,
            DespawnMessageBytes = ReplayBytes.Despawn,
        }
    );

static RunScreenshotRecord Screenshot(
    string id,
    string relativePath,
    DateTimeOffset capturedAtUtc
) =>
    new()
    {
        ScreenshotId = id,
        RunId = "run-screenshot",
        HeroName = "Vanessa",
        IsPrimary = true,
        ImageRelativePath = relativePath,
        CapturedAtLocal = capturedAtUtc.ToOffset(TimeSpan.FromHours(8)),
        CapturedAtUtc = capturedAtUtc,
        Day = 10,
        PlayerRank = "Legendary",
        PlayerRating = 1533,
        PlayerPosition = 41,
        VictoriesAtCapture = 10,
    };

// A fixed gradient; the artifact verifier allows only JPEG's small lossy pixel error.
static void WritePng(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var image = new Image<Rgba32>(96, 64);
    for (var y = 0; y < image.Height; y++)
    for (var x = 0; x < image.Width; x++)
        image[x, y] = PipelineInputs.ScreenshotPixel(x, y);
    image.SaveAsPng(path);
}

static void RoutingKeepsPveOutOfPersistence(string database)
{
    Check.That(
        CapturedReplayRouter.Resolve(
            new PvpBattleManifest { BattleId = "pve", CombatKind = "Combat" }
        ) == CapturedReplayRoute.CurrentNative,
        "PvE captures stay on the in-memory current-native route."
    );
    Check.That(
        CapturedReplayRouter.Resolve(
            new PvpBattleManifest { BattleId = "pvp", CombatKind = "PVPCombat" }
        ) == CapturedReplayRoute.PersistedPvp,
        "PvP captures use the persisted catalog route."
    );
    try
    {
        new PvpBattleSqliteStore(database).Save(
            new PvpBattleManifest { BattleId = "pve", CombatKind = "Combat" }
        );
        throw new InvalidOperationException("The PvP store accepted a PvE manifest.");
    }
    catch (ArgumentException)
    {
        // A routing regression must fail instead of reporting false persistence success.
    }
}

static SealedBundle Sealed(PipelineArtifacts artifacts, string dataRoot, string name, string runId)
{
    var database = PathConstants.RunLogDatabase(dataRoot);
    using var connection = new SqliteConnection($"Data Source={database}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText =
        "SELECT bundle_id, file_name FROM bundle_outbox WHERE run_id=$runId AND status='pending';";
    command.Parameters.AddWithValue("$runId", runId);
    using var reader = command.ExecuteReader();
    if (!reader.Read())
    {
        reader.Close();
        using var job = connection.CreateCommand();
        job.CommandText =
            "SELECT state, last_error_code, last_error_detail FROM bundle_seal_jobs WHERE run_id=$runId;";
        job.Parameters.AddWithValue("$runId", runId);
        using var jobReader = job.ExecuteReader();
        var detail = jobReader.Read()
            ? $"{jobReader.GetString(0)}/{(jobReader.IsDBNull(1) ? "" : jobReader.GetString(1))}/{(jobReader.IsDBNull(2) ? "" : jobReader.GetString(2))}"
            : "job missing";
        throw new InvalidOperationException($"{runId} should seal into the outbox: {detail}");
    }
    var bundleId = reader.GetString(0);
    var bytes = TestInputs.ScratchBytes(
        Path.Combine(PathConstants.BundleOutbox(dataRoot), reader.GetString(1))
    );
    var opened = artifacts.RecordBundle(name, bytes);
    Check.That(opened.Manifest.BundleId == bundleId, "File identity should match the outbox.");
    Check.That(
        RunBundleV5Contract.Open(bytes).Succeeded,
        $"{name} should satisfy the Run Bundle contract."
    );
    var payload = RunPayloadV5Codec.Decode(opened.RunPayload);
    Check.That(payload.RunId == runId, "The payload should preserve the Run identity.");
    return new SealedBundle(bytes, opened, payload);
}

static void ExpireDeadline(string database, string runId) =>
    Execute(
        database,
        $"UPDATE bundle_seal_jobs SET input_deadline_at_utc=datetime('now','-1 second') WHERE run_id='{runId}';"
    );

static void Execute(string database, string sql)
{
    using var connection = new SqliteConnection($"Data Source={database}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

static long Count(string database, string table, string runId) =>
    Number(database, $"SELECT COUNT(*) FROM {table} WHERE run_id='{runId}';");

static long Number(string database, string sql)
{
    using var connection = new SqliteConnection($"Data Source={database}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(command.ExecuteScalar());
}

static void ExpectSqliteConstraint(Action action, string message)
{
    try
    {
        action();
    }
    catch (SqliteException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static PvpBattleCardSetCapture CapturedEmpty() =>
    new() { Status = PvpBattleCaptureStatus.CapturedEmpty };

static async Task VerifyLegacyJsonRecoveryAsync(string root)
{
    Directory.CreateDirectory(root);
    var paths = new TestPaths(root);
    var database = PathConstants.RunLogDatabase(root);
    var store = new RunLogStore(paths);
    var queue = new BundleQueueStore(database);
    var catalog = new PvpBattleSqliteStore(database);
    var replayStore = new CombatReplayPayloadStore(PathConstants.CombatReplays(root));
    var generator = new UlidV5Generator();
    // Clock-relative on purpose: these rows model released 5.5.0 failures 40 days old, inside
    // the 90-day terminal window that BundleSealFailurePolicy measures from now.
    var old = DateTimeOffset.UtcNow.AddDays(-40);
    const string message =
        "Method not found: 'System.String Newtonsoft.Json.Linq.JToken.ToString(Newtonsoft.Json.Formatting)'.";
    for (var i = 0; i < 12; i++)
    {
        store.CreateRun(
            new RunLogCreateRequest
            {
                RunId = $"legacy-{i}",
                StartedAtUtc = old,
                Hero = "Vanessa",
                GameMode = "Ranked",
                PlayerAccountId = "legacy-account",
                BundleScreenshotRequested = i == 0,
                ModVersion = "5.5.0",
            }
        );
        store.CompleteRun(
            $"legacy-{i}",
            new RunLogCompletion { EndedAtUtc = old.AddMinutes(1), Status = "completed" }
        );
    }
    queue.EnsureEligibleJobs(TimeSpan.FromMinutes(2));
    var oldBundle = BuildRunOnlyBundle(
        generator.Next(),
        "legacy-1",
        "legacy-account",
        old.ToUnixTimeMilliseconds()
    );
    var allocation = queue.EnsureAllocation(
        "legacy-1",
        oldBundle.Manifest.BundleId,
        oldBundle.Manifest.CreatedAtMs
    );
    Directory.CreateDirectory(PathConstants.BundleOutbox(root));
    var oldFile = oldBundle.Manifest.BundleId + ".bundle";
    File.WriteAllBytes(Path.Combine(PathConstants.BundleOutbox(root), oldFile), oldBundle.Bytes);
    queue.PublishOutbox(
        allocation,
        new BundleOutboxPublishRecord(
            allocation.BundleId,
            "legacy-1",
            oldFile,
            oldBundle.Sha256Hex,
            oldBundle.ContentDigest,
            oldBundle.Bytes.Length,
            false
        ),
        old
    );
    queue.FailOutboxAndScheduleReseal(allocation.BundleId, "legacy-1", "pending_file_invalid", old);
    for (var i = 0; i < 12; i++)
    {
        queue.EnsureAllocation($"legacy-{i}", generator.Next(), old.ToUnixTimeMilliseconds());
        // Released 5.5.0 rows: the old allocation counted one attempt before the build failed.
        queue.RecordSealFailure(
            $"legacy-{i}",
            BundleSealJobState.TerminalFailure,
            "bundle_build_failed",
            i == 11 ? "Invalid projection" : message,
            1,
            old
        );
    }

    catalog.Save(
        new PvpBattleManifest
        {
            BattleId = "pending-delete",
            RunId = "legacy-0",
            RecordedAtUtc = old,
            CombatKind = "PVPCombat",
            Participants = new PvpBattleParticipants
            {
                PlayerAccountId = "legacy-account",
                PlayerHero = "Vanessa",
                OpponentAccountId = "opponent-account",
                OpponentHero = "Pygmalien",
            },
            Snapshots = new PvpBattleSnapshots
            {
                PlayerHand = CapturedEmpty(),
                PlayerSkills = CapturedEmpty(),
                OpponentHand = CapturedEmpty(),
                OpponentSkills = CapturedEmpty(),
            },
        }
    );
    replayStore.Save(
        new PvpReplayPayload
        {
            BattleId = "pending-delete",
            SpawnMessageBytes = [1],
            CombatMessageBytes = [2],
            DespawnMessageBytes = [3],
        }
    );
    using var connection = new SqliteConnection($"Data Source={database}");
    connection.Open();
    var sourceRemoved = BuildRunOnlyBundle(
        generator.Next(),
        "source-removed",
        "legacy-account",
        old.ToUnixTimeMilliseconds()
    );
    var sourceRemovedFile = sourceRemoved.Manifest.BundleId + ".bundle";
    File.WriteAllBytes(
        Path.Combine(PathConstants.BundleOutbox(root), sourceRemovedFile),
        sourceRemoved.Bytes
    );
    using (var orphan = connection.CreateCommand())
    {
        orphan.CommandText = """
            INSERT INTO bundle_outbox (
                bundle_id, run_id, file_name, content_sha256_hex, content_digest,
                total_bytes, has_screenshot, sealed_at_utc, status
            ) VALUES ($id, 'source-removed', $file, $sha, $digest, $bytes, 0, $sealed, 'pending');
            """;
        orphan.Parameters.AddWithValue("$id", sourceRemoved.Manifest.BundleId);
        orphan.Parameters.AddWithValue("$file", sourceRemovedFile);
        orphan.Parameters.AddWithValue("$sha", sourceRemoved.Sha256Hex);
        orphan.Parameters.AddWithValue("$digest", sourceRemoved.ContentDigest);
        orphan.Parameters.AddWithValue("$bytes", sourceRemoved.Bytes.Length);
        orphan.Parameters.AddWithValue("$sealed", old.ToString("o"));
        orphan.ExecuteNonQuery();
    }
    queue.FailOutboxAndScheduleReseal(
        sourceRemoved.Manifest.BundleId,
        "source-removed",
        "pending_file_invalid",
        old
    );
    using (var seed = connection.CreateCommand())
    {
        seed.CommandText = """
            UPDATE battles SET has_local_payload=0, local_payload_state='delete_pending' WHERE battle_id='pending-delete';
            PRAGMA user_version=2;
            """;
        seed.ExecuteNonQuery();
    }
    var services = new TestServices(paths);
    using var coordinator = new BundleSealCoordinator(services);
    Assert(
        queue.ListWaitingRunIds().Count == 11,
        "Upgrade should restore only JSON failures before sealing."
    );
    using var gate = new ReplayPayloadOperationGate();
    new ReplayPayloadMaintenanceService(
        catalog,
        replayStore,
        gate,
        () => Array.Empty<string>()
    ).Run(DateTimeOffset.UtcNow, CancellationToken.None);
    Assert(
        !replayStore.ListBattleIds().Contains("pending-delete"),
        "Already committed payload deletion still completes after recovery."
    );
    var arms = 0;
    using var armSubscription = services.EventBus.Subscribe<UploadArmRequested>(_ => arms++);
    await coordinator.ReconcileAsync(CancellationToken.None);
    Assert(arms == 1, "A recovered backlog must arm one batch, not one per sealed run.");
    Assert(
        Convert.ToInt32(
            ScalarObject(connection, "SELECT COUNT(*) FROM bundle_outbox WHERE status='pending';")
        ) == 11,
        "Every recoverable run should seal."
    );
    Assert(
        queue.ReadJob("legacy-11")?.State == BundleSealJobState.TerminalFailure,
        "Other failures stay terminal."
    );
    Assert(
        Scalar(
            connection,
            "SELECT status FROM bundle_outbox WHERE bundle_id='" + allocation.BundleId + "';"
        ) == "permanent_failure",
        "The old invalid-file outcome remains terminal while its replacement seals."
    );
    var file = Scalar(
        connection,
        "SELECT file_name FROM bundle_outbox WHERE run_id='legacy-0' AND status='pending';"
    )!;
    var bytes = TestInputs.ScratchBytes(Path.Combine(PathConstants.BundleOutbox(root), file));
    Assert(
        RunBundleV5Contract.Open(bytes).Succeeded,
        "Recovered degraded Bundle should satisfy the shared contract."
    );
    var payload = RunPayloadV5Codec.Decode(BundleV5Codec.Open(bytes).RunPayload);
    Assert(
        payload.Degradation.ScreenshotOmitted
            && payload.Degradation.ReplayOmittedBattleIds.Contains("pending-delete"),
        "Missing screenshot and previously scheduled replay deletion should degrade explicitly."
    );

    using var restarted = new BundleSealCoordinator(services);
    await restarted.ReconcileAsync(CancellationToken.None);
    Assert(
        arms == 1 && queue.ReadJob("legacy-11")?.State == BundleSealJobState.TerminalFailure,
        "Restart must leave sealed and unrelated terminal runs unchanged."
    );

    Assert(
        queue.ReadJob("source-removed") == null
            && File.Exists(Path.Combine(PathConstants.BundleOutbox(root), sourceRemovedFile))
            && Scalar(connection, "SELECT status FROM bundle_outbox WHERE run_id='source-removed';")
                == "permanent_failure",
        "Source-less permanent outboxes are outside automatic reseal recovery."
    );
}

static async Task VerifyRecoveryAsync(string root)
{
    Directory.CreateDirectory(root);
    var paths = new TestPaths(root);
    var store = new RunLogStore(paths);
    var database = PathConstants.RunLogDatabase(root);
    using var coordinator = new BundleSealCoordinator(new TestServices(paths));
    var outboxRoot = PathConstants.BundleOutbox(root);
    Directory.CreateDirectory(outboxRoot);

    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-orphan-adopt",
            StartedAtUtc = PipelineInputs.Base,
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
            StartedAtUtc = PipelineInputs.Base,
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
    // File age is measured against the 24-hour orphan retention, so it stays clock-relative.
    File.SetLastWriteTimeUtc(mismatchPath, DateTime.UtcNow.AddHours(-25));

    store.CreateRun(
        new RunLogCreateRequest
        {
            RunId = "run-invalid-pending",
            StartedAtUtc = PipelineInputs.Base,
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
        invalid.Parameters.AddWithValue("$now", PipelineInputs.Base.ToString("o"));
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
        "INSERT INTO bundle_seal_jobs (run_id, state, player_account_id, screenshot_requested, screenshot_state, input_deadline_at_utc, bundle_id, created_at_ms) VALUES ($runId, 'sealing', $accountId, 0, 'not_requested', $deadline, $bundleId, $createdAtMs);";
    command.Parameters.AddWithValue("$runId", runId);
    command.Parameters.AddWithValue("$accountId", accountId);
    // SQLite datetime() text, the one format input_deadline_at_utc holds.
    command.Parameters.AddWithValue(
        "$deadline",
        PipelineInputs.Base.AddMinutes(2).ToString("yyyy-MM-dd HH:mm:ss")
    );
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

/// <summary>
/// The account id the runs carry and the secrets the 5xx upload body carries; no log line from
/// the pipeline run may contain any of them.
/// </summary>
internal static class LogPrivacy
{
    internal const string AccountId = "account-001";
    internal const string ResponseSecret = "token-secret";
    internal const string AccountSecret = "account-secret";

    internal static void Verify(IReadOnlyList<string> lines)
    {
        Check.That(
            lines.Any(line => line.Contains("event=bundle_pipeline.seal.succeeded"))
                && lines.Any(line => line.Contains("event=bundle_pipeline.upload.degraded"))
                && lines.Any(line => line.Contains("event=bundle_pipeline.upload.succeeded")),
            "The privacy capture must observe the seal, the failed upload, and the accepted upload."
        );
        foreach (var secret in new[] { AccountId, ResponseSecret, AccountSecret })
        {
            var leaked = lines.FirstOrDefault(line =>
                line.Contains(secret, StringComparison.Ordinal)
            );
            Check.That(leaked == null, $"A log line carries '{secret}': {leaked}");
        }
    }
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
        if (request.RequestUri?.AbsolutePath != "/bundles")
            throw new InvalidOperationException($"Upload route drifted: {request.RequestUri}");
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
    public BppConfig Config => null!;
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

/// <summary>
/// Fixed pipeline inputs. Times sit in the future on purpose: EnsureEligibleJobs derives each
/// seal job's input deadline from ended_at_utc + 2 minutes, so a past constant would expire every
/// deadline at once and the screenshot, replay-persistence, and replay-payload waits could not be
/// observed. A deadline expires only through the explicit datetime('now','-1 second') step.
/// </summary>
internal static class PipelineInputs
{
    internal static readonly DateTimeOffset Base = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static Rgba32 ScreenshotPixel(int x, int y) =>
        new((byte)(x * 2), (byte)(y * 3), (byte)((x + y) % 256), 255);
}

// The game's NetMessage constructors assign a random MessageId; fixing it keeps the replay
// uncompressed replay bytes identical across runs.
internal static class ReplayBytes
{
    internal static readonly byte[] Spawn = MessagePackSerializer.Serialize(
        new NetMessageGameSim(new GameSim()) { MessageId = "spawn" },
        MessagePackConfig.Options
    );

    // Frozen online CombatSim input: PTR appends IsDraw to the live serializer's array.
    // The pipeline golden measures preserved replay bytes; CombatReplayLoader still
    // decodes this input with the selected game's serializer, including PTR's default IsDraw.
    internal static readonly byte[] Combat = Convert.FromBase64String(
        "kpiRlJDAwIAAAZCQgJCQpmNvbWJhdA=="
    );
    internal static readonly byte[] Despawn = MessagePackSerializer.Serialize(
        new NetMessageGameSim(new GameSim()) { MessageId = "despawn" },
        MessagePackConfig.Options
    );
}

internal sealed record SealedBundle(byte[] Bytes, OpenedBundleV5 Opened, RunPayloadV5 Payload);
