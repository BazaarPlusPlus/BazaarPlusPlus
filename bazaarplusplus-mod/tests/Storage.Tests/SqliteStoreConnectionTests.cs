#nullable enable
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;

internal static class SqliteStoreConnectionTests
{
    internal static void Run()
    {
        ForeignKeysStayEnforcedOnEveryAcquisition();
    }

    private sealed class ProbeStore : SqliteStoreBase
    {
        internal ProbeStore(string databasePath)
            : base(databasePath) { }

        internal SqliteConnection Acquire() => OpenConnection();

        internal SqliteConnection Acquire(int busyTimeoutMilliseconds) =>
            OpenConnection(busyTimeoutMilliseconds);
    }

    private static void ForeignKeysStayEnforcedOnEveryAcquisition()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bpp-store-connection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new ProbeStore(Path.Combine(root, "run.db"));

            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var connection = store.Acquire();
                Equal(1L, Scalar(connection, "PRAGMA foreign_keys;"), "foreign_keys pragma");
                Equal(2000L, Scalar(connection, "PRAGMA busy_timeout;"), "busy_timeout pragma");
            }

            // A shorter wait applies to its own acquisition only: the pooled connection it used
            // goes back to the default wait on the next default acquisition.
            using (var connection = store.Acquire(250))
            {
                Equal(1L, Scalar(connection, "PRAGMA foreign_keys;"), "short-wait foreign_keys");
                Equal(250L, Scalar(connection, "PRAGMA busy_timeout;"), "short busy_timeout");
            }
            using (var connection = store.Acquire())
                Equal(2000L, Scalar(connection, "PRAGMA busy_timeout;"), "restored busy_timeout");

            using (var connection = store.Acquire())
            {
                // A run_events row without its parent run must be rejected by the FK.
                Throws<SqliteException>(
                    () =>
                        Execute(
                            connection,
                            """
                            INSERT INTO run_events (run_id, seq, ts_at_ms, kind, payload_json)
                            VALUES ('missing-run', 1, 1767225600000, 'run_started', '{}');
                            """
                        ),
                    "orphan run_events insert"
                );

                // And a real parent must cascade its children away on delete.
                Execute(
                    connection,
                    """
                    INSERT INTO runs (
                        run_id, started_at_ms, last_seen_at_ms, status, hero, game_mode
                    ) VALUES (
                        'fk-run', 1767225600000, 1767225600000, 'active', 'Vanessa', 'Ranked'
                    );
                    INSERT INTO run_events (run_id, seq, ts_at_ms, kind, payload_json)
                    VALUES ('fk-run', 1, 1767225600000, 'run_started', '{}');
                    DELETE FROM runs WHERE run_id = 'fk-run';
                    """
                );
                Equal(
                    0L,
                    Scalar(connection, "SELECT COUNT(*) FROM run_events;"),
                    "cascaded run_events"
                );
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
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
