using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BazaarGameShared;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.ModApi.Clients;
using BazaarPlusPlus.Patches.RunLogging;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunScreenshot;
using BazaarPlusPlus.TestSupport;
using BepInEx.Logging;
using MessagePack;
using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public sealed class BundlePipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "bpp-pipeline-" + Guid.NewGuid().ToString("N")
    );

    public BundlePipelineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_shared_projects_load_their_desktop_output()
    {
        var mismatch = SharedTargetPin.Mismatch(SharedTargetPin.Desktop);
        Check.That(mismatch == null, mismatch ?? string.Empty);
    }

    [Fact]
    public async Task The_pipeline_seals_and_uploads_every_run_as_its_golden_records()
    {
        var root = _root;
        var logLines = new List<string>();
        var logSource = new ManualLogSource("bundle-pipeline-privacy");
        logSource.LogEvent += (_, args) => logLines.Add(args.Data?.ToString() ?? string.Empty);
        BppLog.Install(new BepInExLogSink(logSource));

        var artifacts = new PipelineArtifacts();
        var dataRoot = Path.Combine(root, "pipeline");
        Directory.CreateDirectory(dataRoot);
        var paths = new TestPaths(dataRoot);
        var store = new RunLogStore(paths);
        var database = PathConstants.RunLogDatabase(dataRoot);
        var replays = new CombatReplayPayloadStore(PathConstants.CombatReplays(dataRoot));
        using var coordinator = DesktopBundleSealCoordinator.Create(new TestServices(paths));

        RoutingKeepsPveOutOfPersistence(database);

        CreateRun(store, "run-seal-001", minute: 0, screenshotRequested: false);
        store.AppendEvent(
            "run-seal-001",
            new RunLogEvent
            {
                RunId = "run-seal-001",
                Seq = 1,
                Ts = PipelineInputs.Base,
                Kind = "run_started",
                Hero = "Vanessa",
                GameMode = "Ranked",
                ServerBuildId = "server-build-13176",
                GameDataEtag = "0123456789abcdef0123456789abcdef",
            }
        );
        var streamMessage = MessagePackSerializer.Serialize<INetMessage>(
            // The game draws a random message id; the golden needs a fixed one.
            new NetMessageRunInitialized("run-seal-001", "server-build-13176", "prod", "Ranked")
            {
                MessageId = "m01",
            },
            MessagePackConfig.Options
        );
        store.AppendEvent(
            "run-seal-001",
            new RunLogEvent
            {
                RunId = "run-seal-001",
                Seq = 2,
                Ts = PipelineInputs.Base,
                Kind = RunMessageStreamKinds.Command,
                Command = "SendInitializeRun",
                CommandArgs = new Newtonsoft.Json.Linq.JObject { ["playmode"] = "Ranked" },
            }
        );
        store.AppendEvent(
            "run-seal-001",
            new RunLogEvent
            {
                RunId = "run-seal-001",
                Seq = 3,
                Ts = PipelineInputs.Base.AddMilliseconds(250),
                Kind = RunMessageStreamKinds.Message,
                Message = Convert.ToBase64String(streamMessage),
                RespondsToSeq = 2,
            }
        );
        SaveBattle(database, "battle-seal-001", "run-seal-001", minute: 0);
        SealInputs.SaveReplay(replays, "battle-seal-001");
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
            RunBundleV6Contract
                .Open(runOnly.Bytes)
                .Value!.TryGetReplayableBattle("battle-seal-001", out _),
            "The sealed replayable battle should resolve through the shared contract."
        );
        Check.That(
            runOnly.Payload.Battles[0].Replay?.PlayerAttributes?["HealthRegen"] == 12
                && runOnly.Payload.Battles[0].Replay?.PlayerStash
                    is [{ InstanceId: "itm_stash", Tier: "Bronze" } stashCard]
                && stashCard.Attributes["Quest_1"] == 2,
            "The recorded player attributes and stash travel from the Replay Payload into the Bundle."
        );
        Check.That(
            runOnly.Payload.Run.ServerBuildId == "server-build-13176"
                && runOnly.Payload.Run.GameDataEtag == "0123456789abcdef0123456789abcdef",
            "The game version travels from the run_started event into the Run facts."
        );
        var stream = runOnly.Payload.MessageStream;
        Check.That(
            stream is [{ Seq: 2, Command: "SendInitializeRun" } command, { Seq: 3 } answer]
                && command.CommandArgsJson == "{\"playmode\":\"Ranked\"}"
                && answer.RespondsToSeq == 2
                && MessagePackSerializer.Deserialize<INetMessage>(
                    answer.Message,
                    MessagePackConfig.Options
                )
                    is NetMessageRunInitialized { RunId: "run-seal-001" }
                && runOnly
                    .Payload.Events.Select(value => value.Kind)
                    .SequenceEqual(["run_started"]),
            "The stream rows travel into the message stream with the game's bytes, not the events."
        );
        Check.That(
            Count(database, "run_events", "run-seal-001") == 3,
            "A pending Bundle keeps the stream rows a reseal would rebuild it from."
        );
        artifacts.RecordQueue("run-only", database, "run-seal-001");
        SealInputs.Execute(database, "DELETE FROM runs WHERE run_id='run-seal-001';");
        Check.That(
            Count(database, "bundle_outbox", "run-seal-001") == 1,
            "The outbox must not FK-block Run deletion."
        );

        CreateRun(store, "run-screenshot", minute: 60, screenshotRequested: true);
        var capturedAtUtc = PipelineInputs.Base.AddMinutes(61).AddSeconds(50);
        var relativeImage = "2099-01-01/2099-01-01_01-01-50-000_final_run-run-screenshot.png";
        SealInputs.WritePng(Path.Combine(PathConstants.Screenshots(dataRoot), relativeImage));
        var screenshots = new RunScreenshotSqliteStore(database);
        screenshots.Save(
            SealInputs.Screenshot(
                "shot-primary-001",
                "run-screenshot",
                relativeImage,
                capturedAtUtc
            )
        );
        ExpectSqliteConstraint(
            () =>
                screenshots.Save(
                    SealInputs.Screenshot(
                        "shot-primary-002",
                        "run-screenshot",
                        "2099-01-01/duplicate.png",
                        capturedAtUtc
                    )
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
            Number(
                database,
                "SELECT has_screenshot FROM bundle_outbox WHERE run_id='run-screenshot';"
            ) == 1,
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

        CreateRun(store, "run-waits-for-screenshot", minute: 120, screenshotRequested: true);
        CompleteRun(store, "run-waits-for-screenshot", minute: 120);
        await coordinator.ReconcileAsync(CancellationToken.None);
        Check.That(
            Count(database, "bundle_outbox", "run-waits-for-screenshot") == 0,
            "A requested screenshot keeps the job waiting before its deadline."
        );
        artifacts.RecordQueue("screenshot-deadline:waiting", database, "run-waits-for-screenshot");
        SealInputs.ExpireDeadline(database, "run-waits-for-screenshot");
        await coordinator.ReconcileAsync(CancellationToken.None);
        var timedOut = Sealed(
            artifacts,
            dataRoot,
            "screenshot-deadline",
            "run-waits-for-screenshot"
        );
        Check.That(
            timedOut.Opened.Manifest.Screenshot == null
                && timedOut.Payload.Degradation.ScreenshotOmitted,
            "The same job seals Run-only once its deadline expires."
        );
        artifacts.RecordQueue("screenshot-deadline:sealed", database, "run-waits-for-screenshot");

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

        CreateRun(store, "run-corrupt-replay", minute: 240, screenshotRequested: false);
        SaveBattle(database, "battle-corrupt", "run-corrupt-replay", minute: 240);
        SealInputs.SaveReplay(replays, "battle-corrupt");
        File.WriteAllBytes(
            Directory
                .EnumerateFiles(PathConstants.CombatReplays(dataRoot), "battle-corrupt*")
                .Single(),
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
        SealInputs.ExpireDeadline(database, "run-corrupt-replay");
        await coordinator.ReconcileAsync(CancellationToken.None);
        var corrupt = Sealed(artifacts, dataRoot, "corrupt-replay", "run-corrupt-replay");
        Check.That(
            corrupt.Payload.Degradation.ReplayOmittedBattleIds.SequenceEqual(
                new[] { "battle-corrupt" }
            ) && !corrupt.Payload.ReplayableBattleIds.Contains("battle-corrupt"),
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

        // Seal these runs after the upload stage to keep their oversized inputs out of its scenarios.
        await SealOverBudgetRuns(store, database, replays, coordinator, artifacts, dataRoot);

        BppLog.Flush();
        LogPrivacy.Verify(logLines);

        // The golden comes first so a pipeline regression shows as an artifact diff.
        var goldenMismatch = artifacts.GoldenMismatch();
        Check.That(goldenMismatch == null, goldenMismatch ?? string.Empty);
    }

    private static void CreateRun(
        RunLogStore store,
        string runId,
        int minute,
        bool screenshotRequested
    ) =>
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

    private static void CompleteRun(RunLogStore store, string runId, int minute) =>
        store.CompleteRun(
            runId,
            new RunLogCompletion
            {
                EndedAtUtc = PipelineInputs.Base.AddMinutes(minute + 1),
                Status = "completed",
            }
        );

    private static void SaveBattle(
        string database,
        string battleId,
        string runId,
        int minute,
        PvpBattleSnapshots? snapshots = null,
        string? opponentAccountId = "account-002"
    ) =>
        new PvpBattleCatalog(new PvpBattleSqliteStore(database)).Save(
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
                    OpponentAccountId = opponentAccountId,
                    OpponentHero = "Pygmalien",
                },
                Outcome = new PvpBattleOutcome
                {
                    Result = "Win",
                    WinnerCombatantId = "Player",
                    LoserCombatantId = "Opponent",
                },
                Snapshots =
                    snapshots
                    ?? new PvpBattleSnapshots
                    {
                        PlayerHand = CapturedEmpty(),
                        PlayerSkills = CapturedEmpty(),
                        OpponentHand = CapturedEmpty(),
                        OpponentSkills = CapturedEmpty(),
                    },
            }
        );

    // Seeded payloads preserve enough entropy after gzip to keep trim counts stable across zlib versions.
    private static async Task SealOverBudgetRuns(
        RunLogStore store,
        string database,
        CombatReplayPayloadStore replays,
        BundleSealCoordinator coordinator,
        PipelineArtifacts artifacts,
        string dataRoot
    )
    {
        const int BudgetMargin = 64 * 1024;

        const int ReplayEntropy = 600_000;
        CreateRun(store, "run-budget-replays", minute: 600, screenshotRequested: false);
        SaveBattle(database, "budget-extra", "run-budget-replays", 600, opponentAccountId: null);
        replays.Save(BudgetReplay("budget-extra", 300_000, seed: 5300));
        for (var index = 1; index <= 5; index++)
        {
            var battleId = $"budget-projection-{index}";
            SaveBattle(database, battleId, "run-budget-replays", 600 + index);
            replays.Save(BudgetReplay(battleId, ReplayEntropy, seed: 5300 + index));
        }
        store.AppendEvent(
            "run-budget-replays",
            new RunLogEvent
            {
                RunId = "run-budget-replays",
                Seq = 1,
                Ts = PipelineInputs.Base.AddMinutes(600),
                Kind = RunMessageStreamKinds.Message,
                Message = Convert.ToBase64String([1, 2, 3]),
            }
        );
        CompleteRun(store, "run-budget-replays", minute: 606);
        await coordinator.ReconcileAsync(CancellationToken.None);
        var trimmedReplays = Sealed(
            artifacts,
            dataRoot,
            "size-budget-replays",
            "run-budget-replays"
        );
        Check.That(
            trimmedReplays.Payload.Degradation.Categories.SequenceEqual(
                new[]
                {
                    "non_projection_replay_trimmed",
                    "message_stream_omitted",
                    "projection_battle_trimmed",
                }
            )
                && trimmedReplays.Payload.MessageStream == null
                && trimmedReplays.Payload.Degradation.ReplayOmittedBattleIds.SequenceEqual(
                    new[] { "budget-extra", "budget-projection-1", "budget-projection-2" }
                )
                && trimmedReplays.Opened.Manifest.Run.Projection.Battles.Count == 3,
            "The size budget trims the non-projection replay, the stream, then the two oldest projections."
        );
        ExpectBudgetMargin(trimmedReplays, ReplayEntropy, BudgetMargin, "replay trim");

        const int EventEntropy = 200_000;
        const int EventCount = 27;
        CreateRun(store, "run-budget-events", minute: 700, screenshotRequested: false);
        var seq = 0L;
        void Append(string kind, string? body) =>
            store.AppendEvent(
                "run-budget-events",
                new RunLogEvent
                {
                    RunId = "run-budget-events",
                    Seq = ++seq,
                    Ts = PipelineInputs.Base.AddMinutes(700).AddSeconds(seq),
                    Kind = kind,
                    EncounterId = body,
                }
            );
        Append("run_started", null);
        for (var index = 0; index < EventCount; index++)
            Append("budget_padding", SeededBase64(EventEntropy, seed: 5400 + index));
        Append("run_completed", null);
        CompleteRun(store, "run-budget-events", minute: 701);
        await coordinator.ReconcileAsync(CancellationToken.None);
        var trimmedEvents = Sealed(artifacts, dataRoot, "size-budget-events", "run-budget-events");
        var events = trimmedEvents.Payload.Events;
        var omitted = trimmedEvents.Payload.Degradation.EventsOmitted;
        Check.That(
            trimmedEvents.Payload.Degradation.Categories.SequenceEqual(new[] { "events_trimmed" })
                && omitted is > 10 and < EventCount
                && events.Count == EventCount + 2 - omitted
                && events[0].Kind == "run_started"
                && events[^1].Kind == "run_completed"
                && events[1].Seq == 2 + omitted,
            "The size budget drops the oldest non-terminal events and keeps the terminal ones."
        );
        ExpectBudgetMargin(trimmedEvents, EventEntropy, BudgetMargin, "event trim");

        // Trimming every projected replay still overflows, so the budget goes on into the events:
        // their random bytes alone exceed the limit by more than the margin.
        const int MixedEventCount = 13;
        Check.That(
            MixedEventCount * EventEntropy - BundleLimitsV6.MaxRunBytes >= BudgetMargin,
            "The mixed Run must overflow with every projection trimmed."
        );
        CreateRun(store, "run-budget-mixed", minute: 800, screenshotRequested: false);
        for (var index = 1; index <= 2; index++)
        {
            var battleId = $"budget-mixed-{index}";
            SaveBattle(database, battleId, "run-budget-mixed", 800 + index);
            replays.Save(BudgetReplay(battleId, 300_000, seed: 5500 + index));
        }
        var mixedSeq = 0L;
        void AppendMixed(string kind, string? body) =>
            store.AppendEvent(
                "run-budget-mixed",
                new RunLogEvent
                {
                    RunId = "run-budget-mixed",
                    Seq = ++mixedSeq,
                    Ts = PipelineInputs.Base.AddMinutes(800).AddSeconds(mixedSeq),
                    Kind = kind,
                    EncounterId = body,
                }
            );
        AppendMixed("run_started", null);
        for (var index = 0; index < MixedEventCount; index++)
            AppendMixed("budget_padding", SeededBase64(EventEntropy, seed: 5600 + index));
        AppendMixed("run_completed", null);
        CompleteRun(store, "run-budget-mixed", minute: 803);
        await coordinator.ReconcileAsync(CancellationToken.None);
        var trimmedMixed = Sealed(artifacts, dataRoot, "size-budget-mixed", "run-budget-mixed");
        var mixedOmitted = trimmedMixed.Payload.Degradation.EventsOmitted;
        Check.That(
            trimmedMixed.Payload.Degradation.Categories.SequenceEqual(
                new[] { "projection_battle_trimmed", "events_trimmed" }
            )
                && trimmedMixed.Payload.Degradation.ReplayOmittedBattleIds.SequenceEqual(
                    new[] { "budget-mixed-1", "budget-mixed-2" }
                )
                && trimmedMixed.Payload.ReplayableBattleIds.Count == 0
                && trimmedMixed.Opened.Manifest.Run.Projection.Battles.Count == 0
                && mixedOmitted is > 0 and < MixedEventCount
                && trimmedMixed.Payload.Events.Count == MixedEventCount + 2 - mixedOmitted,
            "Projections that cannot fit are all trimmed before the oldest events go."
        );
        ExpectBudgetMargin(trimmedMixed, EventEntropy, BudgetMargin, "mixed trim");
    }

    // The sealed segment fits with the margin to spare, and one fewer trim would put back at least
    // `entropy` random bytes, which no compressor removes, so it would overflow by the margin.
    private static void ExpectBudgetMargin(
        SealedBundle sealedBundle,
        int entropy,
        int margin,
        string name
    )
    {
        var length = sealedBundle.Opened.RunPayload.Length;
        Check.That(
            BundleLimitsV6.MaxRunBytes - length >= margin
                && length + entropy - BundleLimitsV6.MaxRunBytes >= margin,
            $"{name}: the trim decision must stay {margin} bytes from the limit (segment {length})."
        );
    }

    private static PvpReplayPayload BudgetReplay(string battleId, int entropy, int seed) =>
        new()
        {
            BattleId = battleId,
            SpawnMessageBytes = MessagePackSerializer.Serialize(
                new NetMessageGameSim(new GameSim()) { MessageId = SeededBase64(entropy, seed) },
                MessagePackConfig.Options
            ),
            CombatMessageBytes = ReplayBytes.Combat,
            DespawnMessageBytes = ReplayBytes.Despawn,
        };

    private static string SeededBase64(int entropy, int seed)
    {
        var random = new byte[entropy];
        new Random(seed).NextBytes(random);
        return Convert.ToBase64String(random);
    }

    private static void RoutingKeepsPveOutOfPersistence(string database)
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
            new PvpBattleCatalog(new PvpBattleSqliteStore(database)).Save(
                new PvpBattleManifest { BattleId = "pve", CombatKind = "Combat" }
            );
            throw new InvalidOperationException("The PvP store accepted a PvE manifest.");
        }
        catch (ArgumentException) { }
    }

    private static SealedBundle Sealed(
        PipelineArtifacts artifacts,
        string dataRoot,
        string name,
        string runId
    )
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
            RunBundleV6Contract.Open(bytes).Succeeded,
            $"{name} should satisfy the Run Bundle contract."
        );
        var payload = RunPayloadV6Codec.Decode(opened.RunPayload);
        Check.That(payload.RunId == runId, "The payload should preserve the Run identity.");
        return new SealedBundle(bytes, opened, payload);
    }

    private static long Count(string database, string table, string runId) =>
        Number(database, $"SELECT COUNT(*) FROM {table} WHERE run_id='{runId}';");

    private static long Number(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void ExpectSqliteConstraint(Action action, string message)
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

    private static PvpBattleCardSetCapture CapturedEmpty() =>
        new() { Status = PvpBattleCaptureStatus.CapturedEmpty };

    private static PvpBattleCardSetCapture Captured(params CardType[] types) =>
        new()
        {
            Status = PvpBattleCaptureStatus.Captured,
            Items = types
                .Select(
                    (type, index) =>
                        new PvpBattleCardSnapshot
                        {
                            InstanceId = $"card-{index}",
                            TemplateId = Guid.Empty.ToString(),
                            Type = type,
                        }
                )
                .ToList(),
        };

    [Fact]
    public async Task VerifyRunSegmentProjectionAsync()
    {
        var root = Path.Combine(_root, "run-segment");
        Directory.CreateDirectory(root);
        var paths = new TestPaths(root);
        var store = new RunLogStore(paths);
        using var coordinator = DesktopBundleSealCoordinator.Create(new TestServices(paths));
        CreateRun(store, "run-segment", minute: 0, screenshotRequested: false);
        SaveBattle(
            PathConstants.RunLogDatabase(root),
            "battle-segment",
            "run-segment",
            minute: 0,
            new PvpBattleSnapshots
            {
                PlayerHand = Captured(CardType.Item, CardType.SocketEffect, CardType.Item),
                PlayerSkills = Captured(CardType.Skill),
                OpponentHand = CapturedEmpty(),
                OpponentSkills = new PvpBattleCardSetCapture
                {
                    Status = PvpBattleCaptureStatus.Missing,
                },
            }
        );
        store.AppendEvent(
            "run-segment",
            new RunLogEvent
            {
                RunId = "run-segment",
                Seq = 1,
                Ts = PipelineInputs.Base.AddSeconds(5),
                Kind = "state_changed",
            }
        );
        CompleteRun(store, "run-segment", minute: 0);
        var database = PathConstants.RunLogDatabase(root);
        await coordinator.ReconcileAsync(CancellationToken.None);
        SealInputs.ExpireDeadline(database, "run-segment");
        await coordinator.ReconcileAsync(CancellationToken.None);
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        var file = Scalar(
            connection,
            "SELECT file_name FROM bundle_outbox WHERE run_id='run-segment' AND status='pending';"
        )!;
        var bytes = TestInputs.ScratchBytes(Path.Combine(PathConstants.BundleOutbox(root), file));
        var payload = RunPayloadV6Codec.Decode(BundleV6Codec.Open(bytes).RunPayload);
        Assert(
            payload.Run.Seed == null,
            "The V6 database has no seed; the Run segment carries null."
        );
        var started = PipelineInputs.Base.ToUnixTimeMilliseconds();
        Assert(
            payload.Run.StartedAtMs == started && payload.Run.EndedAtMs == started + 60_000,
            "Run start and end must be the database's Unix-millisecond integers."
        );
        var runEvent = payload.Events.Single();
        Assert(
            runEvent.TimestampMs == started + 5_000
                && runEvent.Seq == 1
                && runEvent.Kind == "state_changed",
            "The RunEventV6 envelope carries the event's Unix-millisecond time, sequence, and kind."
        );
        var body = JsonNode.Parse(runEvent.PayloadJson)!.AsObject();
        Assert(
            !body.ContainsKey("schema_version")
                && !body.ContainsKey("run_id")
                && !body.ContainsKey("seq")
                && !body.ContainsKey("ts")
                && !body.ContainsKey("kind"),
            "Event payload_json carries no schema_version and drops the keys the envelope owns."
        );
        var participants = payload.Battles.Single().Participants;
        Assert(
            participants.Player.HandItemCount == 2
                && participants.Player.SkillCount == 1
                && participants.Opponent.HandItemCount == 0
                && participants.Opponent.SkillCount == null,
            "Participant counts are the set's Item or Skill cards, 0 when captured empty, null when Missing."
        );
    }

    [Fact]
    public async Task VerifyRecoveryAsync()
    {
        var root = Path.Combine(_root, "recovery");
        Directory.CreateDirectory(root);
        var paths = new TestPaths(root);
        var store = new RunLogStore(paths);
        var database = PathConstants.RunLogDatabase(root);
        using var coordinator = DesktopBundleSealCoordinator.Create(new TestServices(paths));
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
        InsertSealJob(
            database,
            "run-orphan-adopt",
            adopted.Manifest.BundleId,
            1000,
            "account-adopt"
        );
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
                "INSERT INTO bundle_outbox (bundle_id, run_id, file_name, content_sha256_hex, content_digest, total_bytes, has_screenshot, sealed_at_ms, status, next_attempt_at_ms) VALUES ('invalid-bundle', 'run-invalid-pending', 'invalid.bundle', 'sha', 'digest', 3, 0, $now, 'pending', $now);";
            invalid.Parameters.AddWithValue("$now", PipelineInputs.Base.ToUnixTimeMilliseconds());
            invalid.ExecuteNonQuery();
        }
        File.WriteAllText(Path.Combine(outboxRoot, "invalid.bundle"), "bad");

        await coordinator.ReconcileAsync(CancellationToken.None);

        using var verify = new SqliteConnection($"Data Source={database}");
        verify.Open();
        Assert(
            Scalar(
                verify,
                "SELECT status FROM bundle_outbox WHERE bundle_id='"
                    + adopted.Manifest.BundleId
                    + "';"
            ) == "pending",
            "A matching allocated orphan should be adopted atomically."
        );
        Assert(
            !File.Exists(mismatchPath),
            "An expired allocation-mismatch orphan should be deleted."
        );
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

    private static BundleBuildResultV6 BuildRunOnlyBundle(
        string bundleId,
        string runId,
        string accountId,
        long createdAtMs
    ) =>
        BundleV6Codec.Build(
            new BundleBuildInputV6
            {
                BundleId = bundleId,
                CreatedAtMs = createdAtMs,
                RunId = runId,
                PlayerAccountId = accountId,
                RunPayload = RunPayloadV6Codec.Encode(
                    new RunPayloadV6 { RunId = runId, PlayerAccountId = accountId }
                ),
            }
        );

    private static void InsertSealJob(
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
            "INSERT INTO bundle_seal_jobs (run_id, state, player_account_id, screenshot_requested, screenshot_state, input_deadline_at_ms, bundle_id, created_at_ms) VALUES ($runId, 'sealing', $accountId, 0, 'not_requested', $deadline, $bundleId, $createdAtMs);";
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue(
            "$deadline",
            PipelineInputs.Base.AddMinutes(2).ToUnixTimeMilliseconds()
        );
        command.Parameters.AddWithValue("$bundleId", bundleId);
        command.Parameters.AddWithValue("$createdAtMs", createdAtMs);
        command.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static object? ScalarObject(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Fact]
    public async Task Sealing_stamps_provenance_protects_run_resumed_and_keeps_pve_and_error_frames()
    {
        var dataRoot = Path.Combine(_root, "provenance-and-resume");
        Directory.CreateDirectory(dataRoot);
        var paths = new TestPaths(dataRoot);
        var store = new RunLogStore(paths);

        // 1. Non-oversized run: verify provenance, NetMessageError in stream, and pve_combat_recorded seq interleaving.
        CreateRun(store, "run-pve-err", minute: 10, screenshotRequested: false);
        var seq = 0L;
        store.AppendEvent(
            "run-pve-err",
            new RunLogEvent
            {
                RunId = "run-pve-err",
                Seq = ++seq,
                Ts = PipelineInputs.Base.AddMinutes(10).AddSeconds(seq),
                Kind = "run_started",
                ServerBuildId = "build-1",
                GameDataEtag = "etag-1",
            }
        );
        var openingBytes = MessagePackSerializer.Serialize<INetMessage>(
            new NetMessageGameSim(new GameSim()) { MessageId = "opn" },
            MessagePackConfig.Options
        );
        store.AppendEvent(
            "run-pve-err",
            new RunLogEvent
            {
                RunId = "run-pve-err",
                Seq = ++seq,
                Ts = PipelineInputs.Base.AddMinutes(10).AddSeconds(seq),
                Kind = RunMessageStreamKinds.Message,
                Message = Convert.ToBase64String(openingBytes),
            }
        );
        store.AppendEvent(
            "run-pve-err",
            new RunLogEvent
            {
                RunId = "run-pve-err",
                Seq = ++seq,
                Ts = PipelineInputs.Base.AddMinutes(10).AddSeconds(seq),
                Kind = "pve_combat_recorded",
                Day = 1,
                Hour = 3,
                EncounterId = "monster-1",
                CombatKind = "Combat",
                Winner = "Player",
                Loser = "Opponent",
                FrameCount = 80,
                DurationMs = 4000,
                GoldThresholdsHit = 2,
                XpThresholdsHit = 1,
            }
        );
        var closingBytes = MessagePackSerializer.Serialize<INetMessage>(
            new NetMessageGameSim(new GameSim()) { MessageId = "cls" },
            MessagePackConfig.Options
        );
        store.AppendEvent(
            "run-pve-err",
            new RunLogEvent
            {
                RunId = "run-pve-err",
                Seq = ++seq,
                Ts = PipelineInputs.Base.AddMinutes(10).AddSeconds(seq),
                Kind = RunMessageStreamKinds.Message,
                Message = Convert.ToBase64String(closingBytes),
            }
        );
        var errorBytes = MessagePackSerializer.Serialize<INetMessage>(
            new NetMessageError("err", message: "insufficient_gold"),
            MessagePackConfig.Options
        );
        store.AppendEvent(
            "run-pve-err",
            new RunLogEvent
            {
                RunId = "run-pve-err",
                Seq = ++seq,
                Ts = PipelineInputs.Base.AddMinutes(10).AddSeconds(seq),
                Kind = RunMessageStreamKinds.Message,
                Message = Convert.ToBase64String(errorBytes),
            }
        );
        CompleteRun(store, "run-pve-err", minute: 11);

        // 2. Oversized run with run_resumed between trimmable padding events: verify run_resumed survives trimming.
        const int EventEntropy = 180_000;
        const int EventCount = 18;
        CreateRun(store, "run-budget-resume", minute: 20, screenshotRequested: false);
        var rSeq = 0L;
        void AppendResumeCase(
            string kind,
            string? body,
            string? buildId = null,
            string? etag = null
        ) =>
            store.AppendEvent(
                "run-budget-resume",
                new RunLogEvent
                {
                    RunId = "run-budget-resume",
                    Seq = ++rSeq,
                    Ts = PipelineInputs.Base.AddMinutes(20).AddSeconds(rSeq),
                    Kind = kind,
                    EncounterId = body,
                    ServerBuildId = buildId,
                    GameDataEtag = etag,
                }
            );
        AppendResumeCase("run_started", null, "build-1", "etag-1");
        for (var index = 0; index < 3; index++)
            AppendResumeCase("budget_padding", SeededBase64(EventEntropy, seed: 6100 + index));
        AppendResumeCase("run_resumed", null, "build-2", "etag-2");
        for (var index = 3; index < EventCount; index++)
            AppendResumeCase("budget_padding", SeededBase64(EventEntropy, seed: 6100 + index));
        AppendResumeCase("run_completed", null);
        CompleteRun(store, "run-budget-resume", minute: 21);

        using var coordinator = DesktopBundleSealCoordinator.Create(
            new TestServices(paths),
            bundlerVersion: "6.5.0",
            platform: "desktop"
        );
        await coordinator.ReconcileAsync(CancellationToken.None);

        var sealedPve = OpenSealedWithoutArtifacts(dataRoot, "pve-err-provenance", "run-pve-err");
        Check.That(
            sealedPve.Opened.Manifest.BundlerVersion == "6.5.0"
                && sealedPve.Opened.Manifest.Platform == "desktop"
                && sealedPve.Payload.Run.BundlerVersion == "6.5.0"
                && sealedPve.Payload.Run.Platform == "desktop",
            "Sealing stamps bundler_version and platform on both manifest and RunFactsV6."
        );
        var pveEvent = sealedPve.Payload.Events.Single(e => e.Kind == "pve_combat_recorded");
        var stream = sealedPve.Payload.MessageStream!;
        Check.That(
            stream.Count == 3 && stream[0].Seq < pveEvent.Seq && pveEvent.Seq < stream[1].Seq,
            "pve_combat_recorded interleaves between opening and closing GameSim on the shared seq axis."
        );
        var decodedError = MessagePackSerializer.Deserialize<INetMessage>(
            stream[2].Message!,
            MessagePackConfig.Options
        );
        Check.That(
            decodedError is NetMessageError { Message: "insufficient_gold" },
            "NetMessageError round-trips intact in MessageStream."
        );

        var sealedResume = OpenSealedWithoutArtifacts(
            dataRoot,
            "resume-protected",
            "run-budget-resume"
        );
        Check.That(
            sealedResume.Payload.Degradation.Categories.Contains("events_trimmed")
                && sealedResume.Payload.Degradation.EventsOmitted > 0
                && sealedResume.Payload.Events.Any(e =>
                    e.Kind == "run_resumed"
                    && e.PayloadJson.Contains("build-2", StringComparison.Ordinal)
                    && e.PayloadJson.Contains("etag-2", StringComparison.Ordinal)
                ),
            "run_resumed and its version metadata survive size-budget event trimming."
        );
    }

    private static SealedBundle OpenSealedWithoutArtifacts(
        string dataRoot,
        string name,
        string runId
    )
    {
        var outbox = PathConstants.BundleOutbox(dataRoot);
        var database = PathConstants.RunLogDatabase(dataRoot);
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        var fileName = Scalar(
            connection,
            $"SELECT file_name FROM bundle_outbox WHERE run_id = '{runId}'"
        );
        Check.That(!string.IsNullOrEmpty(fileName), $"{name}: expected an outbox row for {runId}.");
        var bytes = TestInputs.ScratchBytes(Path.Combine(outbox, fileName!));
        var opened = BundleV6Codec.Open(bytes);
        PipelineArtifacts.ValidateManifestSchema(name, opened.ManifestBytes);
        return new SealedBundle(bytes, opened, RunPayloadV6Codec.Decode(opened.RunPayload));
    }

    [Fact]
    public void Command_argument_normalization_matches_wire_dto_for_desired_sockets()
    {
        var selectNull = RunMessageStreamCommandPatch.DescribeArgument(
            "SendSelectItem",
            "desiredSockets",
            null
        );
        var moveNull = RunMessageStreamCommandPatch.DescribeArgument(
            "SendMoveItem",
            "desiredSockets",
            null
        );
        var initGameModeNull = RunMessageStreamCommandPatch.DescribeArgument(
            "SendInitializeRun",
            "gameModeId",
            null
        );
        var selectSectionNull = RunMessageStreamCommandPatch.DescribeArgument(
            "SendSelectItem",
            "targetSection",
            null
        );
        var selectSockets = RunMessageStreamCommandPatch.DescribeArgument(
            "SendSelectItem",
            "desiredSockets",
            new[] { "Socket_1" }
        );

        Check.That(
            Newtonsoft.Json.JsonConvert.SerializeObject(selectNull) == "[]"
                && Newtonsoft.Json.JsonConvert.SerializeObject(moveNull) == "[]",
            "Null desiredSockets for SendSelectItem/SendMoveItem normalize to [] to match Android and wire DTO."
        );
        Check.That(
            initGameModeNull == null && selectSectionNull == null,
            "Other nullable command parameters remain null."
        );
        Check.That(
            Newtonsoft.Json.JsonConvert.SerializeObject(selectSockets) == "[\"Socket_1\"]",
            "Non-null socket lists serialize unchanged."
        );
    }

    [Fact]
    public static async Task VerifyUploadClientAsync()
    {
        var payload = RunPayloadV6Codec.Encode(
            new RunPayloadV6 { RunId = "run-http", PlayerAccountId = "account-http" }
        );
        var built = BundleV6Codec.Build(
            new BundleBuildInputV6
            {
                BundleId = "01J00000000000000000000111",
                CreatedAtMs = 1_785_628_800_000,
                RunId = "run-http",
                PlayerAccountId = "account-http",
                RunPayload = payload,
            }
        );
        var matrix = new (object Answer, BundleUploadDisposition Disposition, string? Code)[]
        {
            (
                Json(
                    HttpStatusCode.Created,
                    "{\"bundle_id\":\"01J00000000000000000000111\",\"run_id\":\"run-http\",\"outcome\":\"stored\"}"
                ),
                BundleUploadDisposition.Uploaded,
                null
            ),
            (
                Json(
                    HttpStatusCode.OK,
                    "{\"bundle_id\":\"01J00000000000000000000111\",\"run_id\":\"run-http\",\"outcome\":\"duplicate\"}"
                ),
                BundleUploadDisposition.Uploaded,
                null
            ),
            (
                Json(
                    HttpStatusCode.Conflict,
                    "{\"error\":{\"code\":\"bundle_id_conflict\",\"message\":\"different\",\"retryable\":false,\"request_id\":\"body-request-id\"}}"
                ),
                BundleUploadDisposition.Permanent,
                null
            ),
            (
                Json(
                    HttpStatusCode.Conflict,
                    "{\"error\":\"bundle_id_conflict\",\"retryable\":false}"
                ),
                BundleUploadDisposition.Transient,
                null
            ),
            (
                Json(
                    HttpStatusCode.ServiceUnavailable,
                    "{\"error\":{\"code\":\"storage_unavailable\",\"message\":\"later\",\"retryable\":true,\"request_id\":\"r\"}}"
                ),
                BundleUploadDisposition.EndpointUnavailable,
                "http_503"
            ),
            (
                new HttpResponseMessage(HttpStatusCode.TooManyRequests),
                BundleUploadDisposition.EndpointUnavailable,
                "http_429"
            ),
            (
                new HttpResponseMessage(HttpStatusCode.RequestTimeout),
                BundleUploadDisposition.EndpointUnavailable,
                "http_408"
            ),
            (
                Json(
                    HttpStatusCode.Created,
                    "{\"bundle_id\":\"01J00000000000000000000999\",\"run_id\":\"run-http\",\"outcome\":\"stored\"}"
                ),
                BundleUploadDisposition.Transient,
                "invalid_success_receipt"
            ),
            (
                Json(
                    HttpStatusCode.Forbidden,
                    "{\"error\":{\"code\":\"busy\",\"message\":\"later\",\"retryable\":true}}"
                ),
                BundleUploadDisposition.Transient,
                "http_403"
            ),
            (
                new HttpRequestException("network is unreachable"),
                BundleUploadDisposition.EndpointUnavailable,
                "transport_error"
            ),
            (
                new TaskCanceledException("request timed out"),
                BundleUploadDisposition.EndpointUnavailable,
                "request_timeout"
            ),
        };
        var handler = new CaptureHandler(new Queue<object>(matrix.Select(entry => entry.Answer)));
        using var session =
            ModApiSession.TryCreate(
                "https://example.test",
                "test",
                platformKey: null,
                "BundleUpload",
                TimeSpan.FromSeconds(120),
                handler
            ) ?? throw new InvalidOperationException("test Mod API session");
        var results = new List<BundleUploadResponse>();
        for (var index = 0; index < matrix.Length; index++)
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
            Assert(
                response.Disposition == matrix[index].Disposition
                    && (matrix[index].Code == null || response.Code == matrix[index].Code),
                $"upload response matrix entry {index} should classify as {matrix[index].Disposition} "
                    + $"{matrix[index].Code}, got {response.Disposition} {response.Code}"
            );
        }
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

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

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

internal sealed class CaptureHandler(Queue<object> answers) : HttpMessageHandler
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
        if (request.Content.Headers.ContentType!.MediaType != BundleLimitsV6.BundleContentType)
            throw new InvalidOperationException("Bundle Content-Type required");
        return answers.Dequeue() switch
        {
            HttpResponseMessage response => response,
            Exception exception => throw exception,
            var other => throw new InvalidOperationException($"Unexpected answer {other}."),
        };
    }
}

internal sealed class TestPaths(string root) : IPathProvider
{
    public string? DataRootDirectoryPath => root;
}

internal sealed class TestServices(IPathProvider paths) : IBppServices
{
    public IBppEventBus EventBus { get; } = new InMemoryBppEventBus();
    public BppSettings Config => null!;
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

// Discovery computes deadlines from the run end. Future fixture times keep inputs waiting until ExpireDeadline.
internal static class PipelineInputs
{
    internal static readonly DateTimeOffset Base = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static Rgba32 ScreenshotPixel(int x, int y) =>
        new((byte)(x * 2), (byte)(y * 3), (byte)((x + y) % 256), 255);
}

// The game's constructors pick random message IDs. Fix them to keep replay bytes deterministic.
internal static class ReplayBytes
{
    internal static readonly byte[] Spawn = MessagePackSerializer.Serialize(
        new NetMessageGameSim(new GameSim()) { MessageId = "spawn" },
        MessagePackConfig.Options
    );

    // This live-serializer input omits IsDraw, which PTR appends and must default when decoding.
    internal static readonly byte[] Combat = Convert.FromBase64String(
        "kpiRlJDAwIAAAZCQgJCQpmNvbWJhdA=="
    );
    internal static readonly byte[] Despawn = MessagePackSerializer.Serialize(
        new NetMessageGameSim(new GameSim()) { MessageId = "despawn" },
        MessagePackConfig.Options
    );
}

internal static class SealInputs
{
    internal static void SaveReplay(CombatReplayPayloadStore replays, string battleId) =>
        replays.Save(
            new PvpReplayPayload
            {
                BattleId = battleId,
                SpawnMessageBytes = ReplayBytes.Spawn,
                CombatMessageBytes = ReplayBytes.Combat,
                DespawnMessageBytes = ReplayBytes.Despawn,
                PlayerAttributes = new Dictionary<string, int> { ["HealthRegen"] = 12 },
                PlayerStash =
                [
                    new PvpBattleCardDocument
                    {
                        InstanceId = "itm_stash",
                        TemplateId = "00000000-0000-0000-0000-000000000077",
                        Type = "Item",
                        Size = "Small",
                        Section = "Stash",
                        Tier = "Bronze",
                        Attributes = new Dictionary<string, int> { ["Quest_1"] = 2 },
                    },
                ],
            }
        );

    internal static RunScreenshotRecord Screenshot(
        string id,
        string runId,
        string relativePath,
        DateTimeOffset capturedAtUtc
    ) =>
        new()
        {
            ScreenshotId = id,
            RunId = runId,
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

    // Use a deterministic gradient to check lossy JPEG output against known pixels.
    internal static void WritePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(96, 64);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            image[x, y] = PipelineInputs.ScreenshotPixel(x, y);
        image.SaveAsPng(path);
    }

    internal static void ExpireDeadline(string database, string runId) =>
        Execute(
            database,
            $"UPDATE bundle_seal_jobs SET input_deadline_at_ms={DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds()} WHERE run_id='{runId}';"
        );

    internal static void Execute(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

internal sealed record SealedBundle(byte[] Bytes, OpenedBundleV6 Opened, RunPayloadV6 Payload);
