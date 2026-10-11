#nullable enable
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class ReplayMaintenanceStorageTests
{
    internal static void Run()
    {
        SchedulesOnlyDurablyUnprotectedPayloadsAndRetriesFileDeletion();
        SchedulingRechecksConcurrentSealOwnershipInsideItsWriteTransaction();
        KeepsReplaysOfEveryRunTheSealEligibilityFixtureProtects();
    }

    // Replay maintenance protects a superset of the fixture's protected Runs: it also keeps
    // replays behind a pending outbox, which installer History cleanup releases. A History-Only
    // Run is never sealed, so its replays are released.
    private static void KeepsReplaysOfEveryRunTheSealEligibilityFixtureProtects()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-replay-seal-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "run.db");
        try
        {
            var cases = BundleSealEligibilityCase.Load();
            using (var setup = new SqliteConnection($"Data Source={databasePath}"))
            {
                setup.Open();
                RunLogSchema.EnsureInitialized(setup);
                foreach (var eligibilityCase in cases)
                {
                    eligibilityCase.Seed(setup);
                    InsertBattle(setup, eligibilityCase.Name, eligibilityCase.Name, 1735689600000L);
                }
            }

            var scheduled = new PvpBattleSqliteStore(databasePath)
                .ScheduleReplayPayloadDeletion(
                    cases.Select(eligibilityCase => eligibilityCase.Name).ToArray(),
                    DateTimeOffset.UtcNow
                )
                .ToHashSet(StringComparer.Ordinal);

            foreach (var eligibilityCase in cases.Where(item => item.ProtectedFromCleanup))
                Assert(
                    !scheduled.Contains(eligibilityCase.Name),
                    $"{eligibilityCase.Name}: replay maintenance scheduled a replay the seal may still need."
                );
            Assert(
                cases.Any(item => !item.ProtectedFromCleanup && scheduled.Contains(item.Name)),
                "The fixture must also release some replays, or this check proves nothing."
            );
            var historyOnly = cases.Where(item => item.UploadPolicy == "history_only").ToArray();
            Assert(historyOnly.Length > 0, "The fixture must cover a History-Only Run.");
            foreach (var eligibilityCase in historyOnly)
                Assert(
                    scheduled.Contains(eligibilityCase.Name),
                    $"{eligibilityCase.Name}: replay maintenance kept the replay of a History-Only Run."
                );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void SchedulingRechecksConcurrentSealOwnershipInsideItsWriteTransaction()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-replay-seal-race-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "run.db");
        try
        {
            using (var setup = new SqliteConnection($"Data Source={databasePath}"))
            {
                setup.Open();
                RunLogSchema.EnsureInitialized(setup);
                InsertRun(setup, "job-created-run", "completed", "Casual");
                InsertRun(setup, "job-transition-run", "completed", "Casual");
                InsertBattle(setup, "job-created", "job-created-run", 1735689600000L);
                InsertBattle(setup, "job-transition", "job-transition-run", 1735776000000L);
                InsertSealJob(setup, "job-transition-run", "waiting");
            }

            var catalog = new PvpBattleSqliteStore(databasePath);
            using var writer = new SqliteConnection($"Data Source={databasePath}");
            writer.Open();
            using var writerTransaction = writer.BeginTransaction(deferred: false);
            Execute(
                writer,
                writerTransaction,
                """
                INSERT INTO bundle_seal_jobs (
                    run_id, state, screenshot_requested, screenshot_state, input_deadline_at_ms
                ) VALUES (
                    'job-created-run', 'waiting', 0, 'not_requested', 1735776000000
                );
                UPDATE bundle_seal_jobs
                SET state='sealing'
                WHERE run_id='job-transition-run';
                """
            );

            using var scheduleStarted = new ManualResetEventSlim();
            var schedule = Task.Run(() =>
            {
                scheduleStarted.Set();
                return catalog.ScheduleReplayPayloadDeletion(
                    ["job-created", "job-transition"],
                    DateTimeOffset.UtcNow
                );
            });
            Assert(scheduleStarted.Wait(TimeSpan.FromSeconds(1)), "schedule task did not start");
            Assert(
                !schedule.Wait(TimeSpan.FromMilliseconds(100)),
                "schedule bypassed the concurrent seal ownership transaction"
            );
            writerTransaction.Commit();

            Equal(0, schedule.GetAwaiter().GetResult().Count, "concurrently protected payloads");
            using (var release = new SqliteConnection($"Data Source={databasePath}"))
            {
                release.Open();
                using var command = release.CreateCommand();
                command.CommandText = "DELETE FROM bundle_seal_jobs;";
                command.ExecuteNonQuery();
            }
            Assert(
                catalog
                    .ScheduleReplayPayloadDeletion(
                        ["job-created", "job-transition"],
                        DateTimeOffset.UtcNow
                    )
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .SequenceEqual(["job-created", "job-transition"]),
                "Casual payloads become eligible after seal ownership is released."
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void SchedulesOnlyDurablyUnprotectedPayloadsAndRetriesFileDeletion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-replay-maintenance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "run.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                RunLogSchema.EnsureInitialized(connection);
                InsertRun(connection, "active-run", "active");
                InsertRun(connection, "seal-eligible-run", "completed");
                InsertRun(connection, "uploaded-run", "completed");
                InsertRun(connection, "waiting-job-run", "completed");
                InsertRun(connection, "terminal-job-run", "completed");
                InsertUploadedOutbox(connection, "uploaded-run");
                InsertSealJob(connection, "waiting-job-run", "waiting");
                InsertSealJob(connection, "terminal-job-run", "terminal_failure");
                InsertBattle(connection, "active", "active-run", 1735689600000L);
                InsertBattle(connection, "seal-eligible", "seal-eligible-run", 1735776000000L);
                InsertBattle(connection, "uploaded", "uploaded-run", 1735862400000L);
                InsertBattle(connection, "unowned", null, 1735948800000L);
                InsertBattle(connection, "waiting-job", "waiting-job-run", 1736035200000L);
                InsertBattle(connection, "terminal-job", "terminal-job-run", 1736121600000L);
            }

            var catalog = new PvpBattleSqliteStore(databasePath);
            var inventory = catalog.ListReplayMaintenanceInventory();
            Equal(6, inventory.Count, "bulk inventory count");
            Assert(
                inventory.All(record => record.PayloadState == ReplayPayloadState.Ready),
                "Fresh payload rows must be ready."
            );

            var now = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);
            var scheduled = catalog.ScheduleReplayPayloadDeletion(
                ["active", "seal-eligible", "uploaded", "unowned", "waiting-job", "terminal-job"],
                now
            );
            Assert(
                scheduled.SequenceEqual(["terminal-job", "unowned", "uploaded"]),
                "Active and potentially sealable runs stay protected while terminal failures release payloads."
            );
            Equal(
                ReplayPayloadState.DeletePending,
                catalog
                    .ListReplayMaintenanceInventory()
                    .Single(record => record.BattleId == "uploaded")
                    .PayloadState,
                "scheduled lifecycle state"
            );

            catalog.CompleteReplayPayloadDeletion(["uploaded"], now.AddSeconds(1));
            var afterFirstAttempt = catalog.ListReplayMaintenanceInventory();
            Equal(
                ReplayPayloadState.Evicted,
                afterFirstAttempt.Single(record => record.BattleId == "uploaded").PayloadState,
                "completed lifecycle state"
            );
            Equal(
                ReplayPayloadState.DeletePending,
                afterFirstAttempt.Single(record => record.BattleId == "unowned").PayloadState,
                "failed file deletion remains retryable"
            );

            catalog.CompleteReplayPayloadDeletion(["unowned"], now.AddSeconds(2));
            Equal(
                ReplayPayloadState.Evicted,
                catalog
                    .ListReplayMaintenanceInventory()
                    .Single(record => record.BattleId == "unowned")
                    .PayloadState,
                "retry completion state"
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void InsertRun(
        SqliteConnection connection,
        string runId,
        string status,
        string gameMode = "Ranked"
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runs (
                run_id, started_at_ms, last_seen_at_ms, status,
                hero, game_mode, build_channel
            ) VALUES (
                $runId, 1735689600000, 1735689600000, $status,
                'Vanessa', $gameMode, 'Online'
            );
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$gameMode", gameMode);
        command.ExecuteNonQuery();
    }

    private static void InsertBattle(
        SqliteConnection connection,
        string battleId,
        string? runId,
        long recordedAt
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO battles (
                battle_id, source, run_id, recorded_at_ms, combat_kind,
                local_payload_state
            ) VALUES (
                $battleId, 'LOCAL', $runId, $recordedAt, 'PVPCombat', 'ready'
            );
            """;
        command.Parameters.AddWithValue("$battleId", battleId);
        command.Parameters.AddWithValue("$runId", (object?)runId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recordedAt", recordedAt);
        command.ExecuteNonQuery();
    }

    private static void InsertUploadedOutbox(SqliteConnection connection, string runId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bundle_outbox (
                bundle_id, run_id, file_name, content_sha256_hex, content_digest,
                total_bytes, has_screenshot, sealed_at_ms, status
            ) VALUES (
                'bundle-uploaded', $runId, 'uploaded.bundle', $sha, $digest,
                1, 0, 1735776000000, 'uploaded'
            );
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$sha", new string('a', 64));
        command.Parameters.AddWithValue(
            "$digest",
            "sha-256=:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=:"
        );
        command.ExecuteNonQuery();
    }

    private static void InsertSealJob(SqliteConnection connection, string runId, string state)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO bundle_seal_jobs (
                run_id, state, screenshot_requested, screenshot_state, input_deadline_at_ms
            ) VALUES ($runId, $state, 0, 'not_requested', 1735776000000);
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$state", state);
        command.ExecuteNonQuery();
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Expected {label} to be '{expected}', got '{actual}'."
            );
    }
}
