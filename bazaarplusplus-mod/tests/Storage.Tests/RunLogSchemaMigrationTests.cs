#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class RunLogSchemaMigrationTests
{
    internal static void Run()
    {
        CreatesFreshVersionTwoAndSupportsConcurrentInitialization();
        UpgradesVersionOneLifecycleStateWithoutLosingRows();
        RejectsUnknownFutureVersionWithoutMutation();
        FailedUpgradeRollsBackVersionAndColumns();
    }

    private static void CreatesFreshVersionTwoAndSupportsConcurrentInitialization()
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
            Equal(2L, Scalar(verify, "PRAGMA user_version;"), "fresh schema version");
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

            Equal(2L, Scalar(connection, "PRAGMA user_version;"), "schema version");
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
