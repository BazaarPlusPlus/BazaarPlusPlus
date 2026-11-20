#nullable enable
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Storage.Sqlite;

// Disposing a connection returns its native handle to the pool. Release pooled handles and
// checkpoint the WAL so the installer can read recent runs from the database file alone.
public static class SqliteShutdown
{
    public static void ReleaseRunLogDatabase(
        string? databasePath,
        SqliteOpenOptions? options = null
    )
    {
        if ((options ?? SqliteOpenOptions.Default).JournalMode != SqliteJournalMode.Wal)
            return;

        // Drop pooled handles first: a checkpoint cannot reclaim WAL frames that
        // idle pooled connections still hold read marks on.
        SqliteConnection.ClearAllPools();

        if (!string.IsNullOrWhiteSpace(databasePath) && File.Exists(databasePath))
            Checkpoint(databasePath!);

        // Disposing the checkpoint connection returns it to the pool, keeping its -shm mapping open.
        SqliteConnection.ClearAllPools();
    }

    private static void Checkpoint(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = """
            PRAGMA busy_timeout = 2000;
            PRAGMA wal_checkpoint(TRUNCATE);
            """;
        command.ExecuteNonQuery();
    }
}
