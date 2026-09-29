using System.IO.Compression;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Bundle;
using BazaarPlusPlus.Storage.BundleQueue;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.TestSupport;
using BepInEx.Logging;
using Microsoft.Data.Sqlite;

// Failure behavior observed through the coordinator, durable queue rows, and structured logs.
internal static class BundleSealFailureTests
{
    private const string JsonMismatch =
        "Method not found: 'System.String Newtonsoft.Json.Linq.JToken.ToString(Newtonsoft.Json.Formatting)'.";

    internal static async Task RunAsync(string root)
    {
        await EnvironmentFailureParksUntilNextLaunchAsync(Path.Combine(root, "environment"));
        await PoisonRunCannotStarveLaterRunsAsync(Path.Combine(root, "poison"));
        await PendingValidationKeepsOutboxOnEnvironmentFailureAsync(Path.Combine(root, "pending"));
        await InvalidReplayDegradesWithLogAsync(Path.Combine(root, "replay"));
        await MaintenanceFailureDoesNotBlockSealingAsync(Path.Combine(root, "maintenance"));
    }

    private static async Task EnvironmentFailureParksUntilNextLaunchAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("env-run", endedMinutesAgo: 10);
        var calls = 0;
        void Broken(BundleSealStage stage, string subject)
        {
            if (stage == BundleSealStage.Build && subject == "env-run")
            {
                calls++;
                throw new InvalidOperationException(
                    "Bundle build failed.",
                    new MissingMethodException(JsonMismatch)
                );
            }
        }

        using (var first = new BundleSealCoordinator(fixture.Services, Broken))
        {
            await first.ReconcileAsync(CancellationToken.None);
            var parked = fixture.Queue.ReadJob("env-run")!;
            Require(
                parked.State == BundleSealJobState.Waiting
                    && parked.LastErrorCode == BundleSealFailurePolicy.EnvironmentBlockedCode
                    && parked.Attempts == 1,
                "An environment failure parks the Run as waiting instead of ending it."
            );
            Require(fixture.OutboxCount("env-run") == 0, "A parked Run publishes nothing.");
            var line = fixture.SingleLine("bundle_pipeline.seal.environment_blocked");
            Require(
                line.Contains(
                    "exception_type=System.InvalidOperationException",
                    StringComparison.Ordinal
                ) && line.Contains("MissingMethodException", StringComparison.Ordinal),
                "The environment log carries the wrapper and the binding failure: " + line
            );
            await first.ReconcileAsync(CancellationToken.None);
            Require(calls == 1, "A parked Run is not retried again in the same launch.");
        }

        await Task.Delay(10);
        using (var second = new BundleSealCoordinator(fixture.Services, Broken))
        {
            await second.ReconcileAsync(CancellationToken.None);
            Require(calls == 2, "The next launch retries a parked Run once.");
            Require(
                fixture.Queue.ReadJob("env-run")!.Attempts == 2,
                "Each launch that stays broken counts one parked launch."
            );
        }

        await Task.Delay(10);
        using var repaired = new BundleSealCoordinator(fixture.Services);
        await repaired.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxCount("env-run") == 1 && fixture.Queue.ReadJob("env-run") == null,
            "A launch with a working runtime seals the parked Run."
        );
    }

    private static async Task PoisonRunCannotStarveLaterRunsAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("poison-run", endedMinutesAgo: 20);
        fixture.AddCompletedRun("healthy-run", endedMinutesAgo: 10);
        var calls = 0;
        using var coordinator = new BundleSealCoordinator(
            fixture.Services,
            (stage, subject) =>
            {
                if (stage == BundleSealStage.Allocation && subject == "poison-run")
                {
                    calls++;
                    throw new InvalidOperationException("poison");
                }
            }
        );

        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxCount("healthy-run") == 1,
            "A later Run seals in the same pass as an earlier failing Run."
        );
        var poisoned = fixture.Queue.ReadJob("poison-run")!;
        Require(
            poisoned.State == BundleSealJobState.Waiting
                && poisoned.LastErrorCode == "seal_allocation_failed"
                && poisoned.Attempts == 1,
            "The failing Run is recorded against itself and keeps waiting."
        );
        Require(
            fixture
                .SingleLine("bundle_pipeline.seal.deferred")
                .Contains("category=seal_allocation_failed", StringComparison.Ordinal),
            "A retried failure is logged."
        );
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(calls == 1, "A failing Run backs off instead of retrying on every pass.");
    }

    private static async Task PendingValidationKeepsOutboxOnEnvironmentFailureAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("pending-run", endedMinutesAgo: 10);
        using (var sealer = new BundleSealCoordinator(fixture.Services))
            await sealer.ReconcileAsync(CancellationToken.None);
        Require(fixture.OutboxCount("pending-run") == 1, "Fixture Run seals.");

        using (
            var broken = new BundleSealCoordinator(
                fixture.Services,
                (stage, _) =>
                {
                    if (stage == BundleSealStage.PendingValidation)
                        throw new MissingMethodException(JsonMismatch);
                }
            )
        )
            await broken.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxStatuses("pending-run").SequenceEqual(["pending"])
                && fixture.Queue.ReadJob("pending-run") == null,
            "A runtime that cannot open Bundles must not invalidate a pending outbox."
        );
        fixture.SingleLine("bundle_pipeline.seal.environment_blocked");

        using (
            var corrupt = new BundleSealCoordinator(
                fixture.Services,
                (stage, _) =>
                {
                    if (stage == BundleSealStage.PendingValidation)
                        throw new InvalidDataException("corrupt");
                }
            )
        )
            await corrupt.ReconcileAsync(CancellationToken.None);
        Require(
            fixture
                .OutboxStatuses("pending-run")
                .OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(["pending", "permanent_failure"]),
            "A corrupt pending file is invalidated and resealed."
        );
        Require(
            fixture
                .SingleLine("bundle_pipeline.seal.degraded")
                .Contains("category=pending_file_invalid", StringComparison.Ordinal),
            "The invalidation is logged."
        );
    }

    private static async Task InvalidReplayDegradesWithLogAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("replay-run", endedMinutesAgo: 10);
        fixture.AddBattle("replay-run", "battle-not-gzip");
        fixture.AddBattle("replay-run", "battle-bad-pack");
        var replayRoot = PathConstants.CombatReplays(root);
        Directory.CreateDirectory(replayRoot);
        File.WriteAllBytes(Path.Combine(replayRoot, "battle-not-gzip.payload.mpack.gz"), [1, 2, 3]);
        File.WriteAllBytes(
            Path.Combine(replayRoot, "battle-bad-pack.payload.mpack.gz"),
            Gzip([0xC1])
        );
        var badPack = new CombatReplayPayloadStore(replayRoot).LoadDetailed("battle-bad-pack");
        Require(
            badPack.Status == FileBackedPayloadLoadStatus.Invalid && badPack.Exception != null,
            "An invalid replay carries its decode exception to the seal policy."
        );

        using var coordinator = new BundleSealCoordinator(fixture.Services);
        await coordinator.ReconcileAsync(CancellationToken.None);
        var file = fixture.PendingFile("replay-run");
        var payload = RunPayloadV5Codec.Decode(
            BundleV5Codec.Open(TestInputs.ScratchBytes(file)).RunPayload
        );
        Require(
            payload.Degradation.ReplayOmittedBattleIds.Contains("battle-not-gzip")
                && payload.Degradation.ReplayOmittedBattleIds.Contains("battle-bad-pack"),
            "Invalid replay data omits those replays after the input deadline."
        );
        Require(
            fixture
                .SingleLine("bundle_pipeline.seal.degraded")
                .Contains("category=replay_invalid", StringComparison.Ordinal),
            "An omitted replay is logged instead of silently dropped."
        );
    }

    private static async Task MaintenanceFailureDoesNotBlockSealingAsync(string root)
    {
        using var fixture = new Fixture(root);
        fixture.AddCompletedRun("maintained-run", endedMinutesAgo: 10);
        fixture.Queue.EnsureEligibleJobs(TimeSpan.FromMinutes(2));
        using var coordinator = new BundleSealCoordinator(
            fixture.Services,
            (stage, _) =>
            {
                if (stage is BundleSealStage.FileRecovery or BundleSealStage.JobDiscovery)
                    throw new IOException("database is locked");
            }
        );
        await coordinator.ReconcileAsync(CancellationToken.None);
        Require(
            fixture.OutboxCount("maintained-run") == 1,
            "File recovery and job discovery failures do not stop waiting Runs from sealing."
        );
        Require(
            fixture
                .Lines("bundle_pipeline.reconcile.failed")
                .Any(line =>
                    line.Contains("category=file_recovery_failed", StringComparison.Ordinal)
                )
                && fixture
                    .Lines("bundle_pipeline.reconcile.failed")
                    .Any(line =>
                        line.Contains("category=job_discovery_failed", StringComparison.Ordinal)
                    ),
            "Each maintenance failure is logged."
        );
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);
        return output.ToArray();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly RunLogStore _runs;
        private readonly string _database;
        private readonly ManualLogSource _logger = new("bundle-seal-failure-tests");
        private readonly List<string> _output = [];

        internal Fixture(string root)
        {
            Directory.CreateDirectory(root);
            var paths = new TestPaths(root);
            Root = root;
            _runs = new RunLogStore(paths);
            _database = PathConstants.RunLogDatabase(root);
            Queue = new BundleQueueStore(_database);
            Services = new TestServices(paths);
            _logger.LogEvent += (_, args) => _output.Add(args.Data?.ToString() ?? string.Empty);
            BppLog.Install(_logger);
        }

        internal string Root { get; }
        internal BundleQueueStore Queue { get; }
        internal TestServices Services { get; }

        internal void AddCompletedRun(string runId, int endedMinutesAgo)
        {
            var ended = DateTimeOffset.UtcNow.AddMinutes(-endedMinutesAgo);
            _runs.CreateRun(
                new RunLogCreateRequest
                {
                    RunId = runId,
                    StartedAtUtc = ended.AddMinutes(-5),
                    Hero = "Vanessa",
                    GameMode = "Ranked",
                    PlayerAccountId = "account-failure",
                    ModVersion = "5.7.0",
                }
            );
            _runs.CompleteRun(
                runId,
                new RunLogCompletion { EndedAtUtc = ended, Status = "completed" }
            );
        }

        internal void AddBattle(string runId, string battleId) =>
            new PvpBattleSqliteStore(_database).Save(
                new PvpBattleManifest
                {
                    BattleId = battleId,
                    RunId = runId,
                    RecordedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-12),
                    CombatKind = "PVPCombat",
                    Day = 1,
                    Hour = 1,
                    Participants = new PvpBattleParticipants
                    {
                        PlayerName = "Player",
                        PlayerAccountId = "account-failure",
                        PlayerHero = "Vanessa",
                        OpponentName = "Opponent",
                        OpponentAccountId = "account-opponent",
                        OpponentHero = "Pygmalien",
                    },
                    Outcome = new PvpBattleOutcome { Result = "Win" },
                    Snapshots = new PvpBattleSnapshots
                    {
                        PlayerHand = Empty(),
                        PlayerSkills = Empty(),
                        OpponentHand = Empty(),
                        OpponentSkills = Empty(),
                    },
                }
            );

        internal int OutboxCount(string runId) =>
            OutboxStatuses(runId).Count(status => status == "pending");

        internal IReadOnlyList<string> OutboxStatuses(string runId) =>
            Query("SELECT status FROM bundle_outbox WHERE run_id=$runId;", runId);

        internal string PendingFile(string runId) =>
            Path.Combine(
                PathConstants.BundleOutbox(Root),
                Query(
                        "SELECT file_name FROM bundle_outbox WHERE run_id=$runId AND status='pending';",
                        runId
                    )
                    .Single()
            );

        internal IReadOnlyList<string> Lines(string eventId) =>
            _output
                .Where(line => line.Contains("event=" + eventId, StringComparison.Ordinal))
                .ToArray();

        internal string SingleLine(string eventId)
        {
            var lines = Lines(eventId);
            Require(
                lines.Count == 1,
                $"Expected one {eventId} line:\n" + string.Join("\n", _output)
            );
            return lines[0];
        }

        public void Dispose()
        {
            _logger.Dispose();
            SqliteConnection.ClearAllPools();
        }

        private IReadOnlyList<string> Query(string sql, string runId)
        {
            using var connection = new SqliteConnection($"Data Source={_database}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$runId", runId);
            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
                values.Add(reader.GetString(0));
            return values;
        }

        private static PvpBattleCardSetCapture Empty() =>
            new() { Status = PvpBattleCaptureStatus.CapturedEmpty };
    }
}
