#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class RunLogSchemaInitializationTests
{
    internal static void Run()
    {
        CreatesFreshCurrentVersionAndSupportsConcurrentInitialization();
        RejectsUnknownFutureVersionWithoutMutation();
        RejectsAV5DatabaseWithoutMutation();
        ClosedValueSetsRejectOtherValues();
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
            Equal(
                0L,
                Scalar(verify, "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger';"),
                "no lifecycle triggers"
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

    // A V5 database (user_version 3) is imported, never opened in place.
    private static void RejectsAV5DatabaseWithoutMutation()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Execute(connection, "PRAGMA user_version=3; CREATE TABLE runs(run_id TEXT PRIMARY KEY);");

        Throws<InvalidOperationException>(
            () => RunLogSchema.EnsureInitialized(connection),
            "V5 version 3"
        );
        Equal(3L, Scalar(connection, "PRAGMA user_version;"), "V5 version retained");
        Equal(
            0L,
            Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name='battles';"),
            "V5 schema is not mutated"
        );
    }

    private static void ClosedValueSetsRejectOtherValues()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        RunLogSchema.EnsureInitialized(connection);
        Execute(
            connection,
            """
            INSERT INTO runs (run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode)
            VALUES ('run', 0, 0, 'active', 'Vanessa', 'Ranked');
            """
        );
        Equal(
            "bundle",
            Text(connection, "SELECT upload_policy FROM runs WHERE run_id='run';"),
            "default upload policy"
        );
        Execute(connection, "UPDATE runs SET upload_policy='history_only' WHERE run_id='run';");
        Throws<SqliteException>(
            () => Execute(connection, "UPDATE runs SET upload_policy='none' WHERE run_id='run';"),
            "unknown upload policy"
        );
        Throws<SqliteException>(
            () => Execute(connection, "UPDATE runs SET status='finished' WHERE run_id='run';"),
            "unknown run status"
        );
        Throws<SqliteException>(
            () =>
                Execute(
                    connection,
                    """
                    INSERT INTO combat_replay_videos (
                        video_id, battle_id, source, video_relative_path, width, height, fps,
                        codec, started_at_ms, status
                    ) VALUES ('video', 'battle', 'LocalSaved', 'a.mp4', 1, 1, 30, 'h264', 0,
                              'completed');
                    """
                ),
            "unknown video status"
        );
        Throws<SqliteException>(
            () =>
                Execute(
                    connection,
                    """
                    INSERT INTO combat_replay_videos (
                        video_id, battle_id, source, video_relative_path, width, height, fps,
                        codec, started_at_ms, status, file_state
                    ) VALUES ('video', 'battle', 'LocalSaved', 'a.mp4', 1, 1, 30, 'h264', 0,
                              'COMPLETED', 'deleted');
                    """
                ),
            "deleted video file state"
        );
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
                        battle_id, source, recorded_at_ms, combat_kind, local_payload_state
                    ) VALUES (
                        '{label}-null-insert', 'LOCAL', 1787443200000, 'PVPCombat', NULL
                    );
                    """
                ),
            $"{label} null lifecycle insert"
        );
        Execute(
            connection,
            $"""
            INSERT INTO battles (
                battle_id, source, recorded_at_ms, combat_kind, local_payload_state
            ) VALUES (
                '{label}-valid', 'LOCAL', 1787443200000, 'PVPCombat', 'missing'
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
