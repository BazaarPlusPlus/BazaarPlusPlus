#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class RunLogSchemaMigrationTests
{
    internal static void Run()
    {
        CreatesFreshCurrentVersionAndSupportsConcurrentInitialization();
        UpgradesVersionOneLifecycleStateWithoutLosingRows();
        RejectsUnknownFutureVersionWithoutMutation();
        FailedUpgradeRollsBackVersionAndColumns();
        RecoversOnlyLegacyJsonFailuresOnce();
        FailedJsonRecoveryRollsBack();
    }

    private const string JsonMissingMethod =
        "Method not found: 'System.String Newtonsoft.Json.Linq.JToken.ToString(Newtonsoft.Json.Formatting)'.";

    private static void SeedJsonFailures(SqliteConnection connection)
    {
        RunLogSchema.EnsureInitialized(connection);
        Execute(
            connection,
            """
            INSERT INTO runs (run_id, started_at_utc, last_seen_at_utc, status, completed, hero, game_mode)
            VALUES ('json', '2026-01-01', '2026-01-01', 'completed', 1, 'Vanessa', 'Ranked'),
                   ('other', '2026-01-01', '2026-01-01', 'completed', 1, 'Vanessa', 'Ranked'),
                   ('uploaded', '2026-01-01', '2026-01-01', 'completed', 1, 'Vanessa', 'Ranked'),
                   ('pending', '2026-01-01', '2026-01-01', 'completed', 1, 'Vanessa', 'Ranked'),
                   ('null-detail', '2026-01-01', '2026-01-01', 'completed', 1, 'Vanessa', 'Ranked'),
                   ('other-code', '2026-01-01', '2026-01-01', 'completed', 1, 'Vanessa', 'Ranked'),
                   ('other-library', '2026-01-01', '2026-01-01', 'completed', 1, 'Vanessa', 'Ranked');
            INSERT INTO bundle_seal_jobs (
                run_id, state, screenshot_requested, screenshot_state, input_deadline_at_utc,
                bundle_id, created_at_ms, attempts, last_error_code
            ) SELECT run_id, 'terminal_failure', 1, 'available', '2026-01-01T00:02:00Z',
                     run_id || '-allocation', 1234, 1, 'bundle_build_failed' FROM runs;
            INSERT INTO bundle_outbox (
                bundle_id, run_id, file_name, content_sha256_hex, content_digest,
                total_bytes, has_screenshot, sealed_at_utc, status
            ) VALUES ('uploaded-bundle', 'uploaded', 'uploaded.bundle', 'sha', 'digest', 1, 0,
                      '2026-01-01', 'uploaded'),
                     ('pending-bundle', 'pending', 'pending.bundle', 'sha', 'digest', 1, 0,
                      '2026-01-01', 'pending');
            PRAGMA user_version = 2;
            """
        );
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE bundle_seal_jobs SET last_error_detail = $message;
            UPDATE bundle_seal_jobs SET last_error_detail = 'Invalid projection' WHERE run_id = 'other';
            UPDATE bundle_seal_jobs SET last_error_detail = NULL WHERE run_id = 'null-detail';
            UPDATE bundle_seal_jobs SET last_error_code = 'payload_compose_failed' WHERE run_id = 'other-code';
            UPDATE bundle_seal_jobs SET last_error_detail = 'Method not found: Other.Library.Method()'
            WHERE run_id = 'other-library';
            """;
        // Captured from CoreCLR by compiling against 13.0.4 and running with 13.0.2.
        command.Parameters.AddWithValue("$message", JsonMissingMethod);
        command.ExecuteNonQuery();
    }

    private static void RecoversOnlyLegacyJsonFailuresOnce()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SeedJsonFailures(connection);

        RunLogSchema.EnsureInitialized(connection);

        Equal(
            "waiting",
            Text(connection, "SELECT state FROM bundle_seal_jobs WHERE run_id='json';"),
            "JSON recovery"
        );
        Equal(
            1L,
            Scalar(connection, "SELECT COUNT(*) FROM bundle_seal_jobs WHERE state='waiting';"),
            "narrow match"
        );
        Equal(
            6L,
            Scalar(
                connection,
                "SELECT COUNT(*) FROM bundle_seal_jobs WHERE state='terminal_failure';"
            ),
            "other terminal rows unchanged"
        );
        Equal(
            "json-allocation",
            Text(connection, "SELECT bundle_id FROM bundle_seal_jobs WHERE run_id='json';"),
            "stable allocation"
        );
        Equal(
            1234L,
            Scalar(connection, "SELECT created_at_ms FROM bundle_seal_jobs WHERE run_id='json';"),
            "stable creation time"
        );
        // The same instant, rewritten to datetime() text by BundleQueueStore.NormalizeSealJobDeadlines.
        Equal(
            "2026-01-01 00:02:00",
            Text(
                connection,
                "SELECT input_deadline_at_utc FROM bundle_seal_jobs WHERE run_id='json';"
            ),
            "original deadline"
        );
        Equal(
            "available",
            Text(connection, "SELECT screenshot_state FROM bundle_seal_jobs WHERE run_id='json';"),
            "screenshot state retained"
        );
        Equal(
            1L,
            Scalar(connection, "SELECT attempts FROM bundle_seal_jobs WHERE run_id='json';"),
            "attempt history retained"
        );
        Equal<string?>(
            null,
            Text(connection, "SELECT last_error_code FROM bundle_seal_jobs WHERE run_id='json';"),
            "error cleared"
        );
        Equal(
            (long)RunLogSchema.LocalDatabaseSchemaVersion,
            Scalar(connection, "PRAGMA user_version;"),
            "recovery committed with version"
        );

        using var failAgain = connection.CreateCommand();
        failAgain.CommandText =
            "UPDATE bundle_seal_jobs SET state='terminal_failure', last_error_code='bundle_build_failed', last_error_detail=$message WHERE run_id='json';";
        failAgain.Parameters.AddWithValue("$message", JsonMissingMethod);
        failAgain.ExecuteNonQuery();
        RunLogSchema.EnsureInitialized(connection);
        Equal(
            0L,
            Scalar(connection, "SELECT COUNT(*) FROM bundle_seal_jobs WHERE state='waiting';"),
            "a later JSON failure never resurrects"
        );
    }

    private static void FailedJsonRecoveryRollsBack()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SeedJsonFailures(connection);
        Execute(
            connection,
            """
            CREATE TRIGGER reject_recovery BEFORE UPDATE OF state ON bundle_seal_jobs
            BEGIN SELECT RAISE(ABORT, 'recovery interrupted'); END;
            """
        );
        Throws<SqliteException>(
            () => RunLogSchema.EnsureInitialized(connection),
            "failed recovery"
        );
        Equal(2L, Scalar(connection, "PRAGMA user_version;"), "recovery version rollback");
        Equal(
            "terminal_failure",
            Text(connection, "SELECT state FROM bundle_seal_jobs WHERE run_id='json';"),
            "recovery state rollback"
        );
        Execute(connection, "DROP TRIGGER reject_recovery;");
        RunLogSchema.EnsureInitialized(connection);
        Equal(
            "waiting",
            Text(connection, "SELECT state FROM bundle_seal_jobs WHERE run_id='json';"),
            "retry after rollback"
        );
    }

    private static void CreatesFreshCurrentVersionAndSupportsConcurrentInitialization()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-schema-fresh-{Guid.NewGuid():N}.db"
        );
        try
        {
            Task.WaitAll(
                Enumerable
                    .Range(0, 8)
                    .Select(_ =>
                        Task.Run(() =>
                        {
                            using var connection = new SqliteConnection(
                                $"Data Source={databasePath}"
                            );
                            connection.Open();
                            RunLogSchema.EnsureInitialized(connection);
                        })
                    )
                    .ToArray()
            );

            using var verify = new SqliteConnection($"Data Source={databasePath}");
            verify.Open();
            Equal(
                (long)RunLogSchema.LocalDatabaseSchemaVersion,
                Scalar(verify, "PRAGMA user_version;"),
                "fresh schema version"
            );
            Equal(
                1L,
                Scalar(
                    verify,
                    "SELECT COUNT(*) FROM pragma_table_info('battles') WHERE name='local_payload_state';"
                ),
                "fresh payload lifecycle column"
            );
            Equal(
                1L,
                Scalar(
                    verify,
                    "SELECT COUNT(*) FROM pragma_table_info('combat_replay_videos') WHERE name='attachment_state';"
                ),
                "fresh video attachment column"
            );
            AssertLocalPayloadLifecycleRejectsNull(verify, "fresh schema");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static void UpgradesVersionOneLifecycleStateWithoutLosingRows()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-schema-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "run.db");
        try
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            Execute(
                connection,
                """
                PRAGMA user_version = 1;
                CREATE TABLE battles (
                    battle_id TEXT PRIMARY KEY,
                    remote_battle_id TEXT NULL,
                    uploader_account_id TEXT NULL,
                    source TEXT NOT NULL,
                    run_id TEXT NULL,
                    local_player_account_id TEXT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    combat_kind TEXT NOT NULL,
                    has_local_payload INTEGER NOT NULL DEFAULT 0,
                    deleted_at_utc TEXT NULL,
                    day INTEGER NULL,
                    result TEXT NULL,
                    winner_combatant_id TEXT NULL
                );
                CREATE TABLE combat_replay_videos (
                    video_id TEXT PRIMARY KEY,
                    battle_id TEXT NOT NULL,
                    source TEXT NOT NULL,
                    video_relative_path TEXT NOT NULL,
                    width INTEGER NOT NULL,
                    height INTEGER NOT NULL,
                    fps INTEGER NOT NULL,
                    codec TEXT NOT NULL,
                    crf INTEGER NULL,
                    preset TEXT NULL,
                    started_at_utc TEXT NOT NULL,
                    ended_at_utc TEXT NULL,
                    duration_ms INTEGER NULL,
                    captured_frames INTEGER NOT NULL DEFAULT 0,
                    dropped_frames INTEGER NOT NULL DEFAULT 0,
                    file_size_bytes INTEGER NULL,
                    status TEXT NOT NULL,
                    error TEXT NULL
                );
                INSERT INTO battles (
                    battle_id, source, recorded_at_utc, combat_kind, has_local_payload
                ) VALUES
                    ('ready-battle', 'LOCAL', '2026-01-01T00:00:00Z', 'PVPCombat', 1),
                    ('missing-battle', 'LOCAL', '2026-01-02T00:00:00Z', 'PVPCombat', 0),
                    ('ghost-battle', 'GHOST', '2026-01-03T00:00:00Z', 'PVPCombat', 0);
                INSERT INTO combat_replay_videos (
                    video_id, battle_id, source, video_relative_path, width, height, fps, codec,
                    started_at_utc, status
                ) VALUES
                    ('attached-video', 'ready-battle', 'LocalSaved', '2026/a.mp4', 1, 1, 30,
                     'h264', '2026-01-01T00:00:00Z', 'COMPLETED'),
                    ('detached-video', 'deleted-battle', 'LocalSaved', '2026/b.mp4', 1, 1, 30,
                     'h264', '2026-01-01T00:00:00Z', 'COMPLETED');
                """
            );

            RunLogSchema.EnsureInitialized(connection);

            Equal(
                (long)RunLogSchema.LocalDatabaseSchemaVersion,
                Scalar(connection, "PRAGMA user_version;"),
                "schema version"
            );
            Equal(
                "ready",
                Text(
                    connection,
                    "SELECT local_payload_state FROM battles WHERE battle_id='ready-battle';"
                ),
                "ready payload backfill"
            );
            Equal(
                "missing",
                Text(
                    connection,
                    "SELECT local_payload_state FROM battles WHERE battle_id='missing-battle';"
                ),
                "missing payload backfill"
            );
            Equal(
                null,
                Text(
                    connection,
                    "SELECT local_payload_state FROM battles WHERE battle_id='ghost-battle';"
                ),
                "ghost payload state"
            );
            Equal(
                "attached",
                Text(
                    connection,
                    "SELECT attachment_state FROM combat_replay_videos WHERE video_id='attached-video';"
                ),
                "attached video backfill"
            );
            Equal(
                "detached",
                Text(
                    connection,
                    "SELECT attachment_state FROM combat_replay_videos WHERE video_id='detached-video';"
                ),
                "detached video backfill"
            );
            Equal(
                TimeSpan.Zero,
                DateTimeOffset
                    .Parse(
                        Text(
                            connection,
                            "SELECT detached_at_utc FROM combat_replay_videos WHERE video_id='detached-video';"
                        )!
                    )
                    .Offset,
                "detached video backfill UTC offset"
            );
            Equal(
                "pending",
                Text(
                    connection,
                    "SELECT file_state FROM combat_replay_videos WHERE video_id='attached-video';"
                ),
                "video file state awaits reconcile"
            );

            RunLogSchema.EnsureInitialized(connection);
            Equal(3L, Scalar(connection, "SELECT COUNT(*) FROM battles;"), "repeat ensure rows");
            Equal(
                2L,
                Scalar(connection, "SELECT COUNT(*) FROM combat_replay_videos;"),
                "repeat ensure videos"
            );
            AssertLocalPayloadLifecycleRejectsNull(connection, "upgraded schema");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void RejectsUnknownFutureVersionWithoutMutation()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-schema-future-{Guid.NewGuid():N}.db"
        );
        try
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            Execute(connection, "PRAGMA user_version=99; CREATE TABLE sentinel(value TEXT);");

            Throws<InvalidOperationException>(
                () => RunLogSchema.EnsureInitialized(connection),
                "unknown future version"
            );
            Equal(99L, Scalar(connection, "PRAGMA user_version;"), "future version retained");
            Equal(
                0L,
                Scalar(
                    connection,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='runs';"
                ),
                "future schema is not mutated"
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static void FailedUpgradeRollsBackVersionAndColumns()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-schema-rollback-{Guid.NewGuid():N}.db"
        );
        try
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            Execute(
                connection,
                """
                PRAGMA user_version=1;
                CREATE TABLE battles (
                    battle_id TEXT PRIMARY KEY,
                    source TEXT NOT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    combat_kind TEXT NOT NULL,
                    has_local_payload INTEGER NOT NULL DEFAULT 0,
                    deleted_at_utc TEXT NULL,
                    day INTEGER NULL,
                    result TEXT NULL,
                    winner_combatant_id TEXT NULL
                );
                """
            );

            Throws<SqliteException>(
                () => RunLogSchema.EnsureInitialized(connection),
                "failed V1 upgrade"
            );
            Equal(1L, Scalar(connection, "PRAGMA user_version;"), "rollback schema version");
            Equal(
                0L,
                Scalar(
                    connection,
                    "SELECT COUNT(*) FROM pragma_table_info('battles') WHERE name='local_payload_state';"
                ),
                "rollback added columns"
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void AssertLocalPayloadLifecycleRejectsNull(
        SqliteConnection connection,
        string label
    )
    {
        Throws<SqliteException>(
            () =>
                Execute(
                    connection,
                    $"""
                    INSERT INTO battles (
                        battle_id, source, recorded_at_utc, combat_kind,
                        has_local_payload, local_payload_state
                    ) VALUES (
                        '{label}-null-insert', 'LOCAL', '2026-08-23T00:00:00Z',
                        'PVPCombat', 0, NULL
                    );
                    """
                ),
            $"{label} null lifecycle insert"
        );
        Execute(
            connection,
            $"""
            INSERT INTO battles (
                battle_id, source, recorded_at_utc, combat_kind,
                has_local_payload, local_payload_state
            ) VALUES (
                '{label}-valid', 'LOCAL', '2026-08-23T00:00:00Z',
                'PVPCombat', 0, 'missing'
            );
            """
        );
        Throws<SqliteException>(
            () =>
                Execute(
                    connection,
                    $"UPDATE battles SET local_payload_state=NULL WHERE battle_id='{label}-valid';"
                ),
            $"{label} null lifecycle update"
        );
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string? Text(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value == null || value == DBNull.Value ? null : Convert.ToString(value);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"Expected {label} to be '{expected}', got '{actual}'."
            );
    }

    private static void Throws<T>(Action action, string label)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {label} to throw {typeof(T).Name}.");
    }
}
