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
                InsertRun(setup, "job-created-run", "completed", completed: true, "Casual");
                InsertRun(setup, "job-transition-run", "completed", completed: true, "Casual");
                InsertBattle(setup, "job-created", "job-created-run", "2025-01-01T00:00:00Z");
                InsertBattle(setup, "job-transition", "job-transition-run", "2025-01-02T00:00:00Z");
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
                    run_id, state, screenshot_requested, screenshot_state, input_deadline_at_utc
                ) VALUES (
                    'job-created-run', 'waiting', 0, 'not_requested', '2025-01-02T00:00:00Z'
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
                InsertRun(connection, "active-run", "active", completed: false);
                InsertRun(connection, "seal-eligible-run", "completed", completed: true);
                InsertRun(connection, "uploaded-run", "completed", completed: true);
                InsertRun(connection, "waiting-job-run", "completed", completed: true);
                InsertRun(connection, "terminal-job-run", "completed", completed: true);
                InsertUploadedOutbox(connection, "uploaded-run");
                InsertSealJob(connection, "waiting-job-run", "waiting");
                InsertSealJob(connection, "terminal-job-run", "terminal_failure");
                InsertBattle(connection, "active", "active-run", "2025-01-01T00:00:00Z");
                InsertBattle(
                    connection,
                    "seal-eligible",
                    "seal-eligible-run",
                    "2025-01-02T00:00:00Z"
                );
                InsertBattle(connection, "uploaded", "uploaded-run", "2025-01-03T00:00:00Z");
                InsertBattle(connection, "unowned", null, "2025-01-04T00:00:00Z");
                InsertBattle(connection, "waiting-job", "waiting-job-run", "2025-01-05T00:00:00Z");
                InsertBattle(
                    connection,
                    "terminal-job",
                    "terminal-job-run",
                    "2025-01-06T00:00:00Z"
                );
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
            Assert(
                !afterFirstAttempt.Single(record => record.BattleId == "unowned").HasLocalPayload,
                "History eligibility must turn off before file deletion."
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
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void InsertRun(
        SqliteConnection connection,
        string runId,
        string status,
        bool completed,
        string gameMode = "Ranked"
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runs (
                run_id, started_at_utc, last_seen_at_utc, status, completed,
                hero, game_mode, build_channel
            ) VALUES (
                $runId, '2025-01-01T00:00:00Z', '2025-01-01T00:00:00Z', $status, $completed,
                'Vanessa', $gameMode, 'Online'
            );
            """;
        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$completed", completed ? 1 : 0);
        command.Parameters.AddWithValue("$gameMode", gameMode);
        command.ExecuteNonQuery();
    }

    private static void InsertBattle(
        SqliteConnection connection,
        string battleId,
        string? runId,
        string recordedAt
    )
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO battles (
                battle_id, source, run_id, recorded_at_utc, combat_kind,
                has_local_payload, local_payload_state
            ) VALUES (
                $battleId, 'LOCAL', $runId, $recordedAt, 'PVPCombat', 1, 'ready'
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
                total_bytes, has_screenshot, sealed_at_utc, status
            ) VALUES (
                'bundle-uploaded', $runId, 'uploaded.bundle', $sha, $digest,
                1, 0, '2025-01-02T00:00:00Z', 'uploaded'
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
                run_id, state, screenshot_requested, screenshot_state, input_deadline_at_utc
            ) VALUES ($runId, $state, 0, 'not_requested', '2025-01-02T00:00:00Z');
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
