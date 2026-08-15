#nullable enable
using Microsoft.Data.Sqlite;

namespace BazaarPlusPlus.Storage.Sqlite;

/// <summary>
/// Hands the run-log database over to other processes on shutdown.
/// </summary>
/// <remarks>
/// The installer opens this database directly while the game owns it. Two
/// details make an unmanaged exit hostile to that reader: Microsoft.Data.Sqlite
/// pools connections, so disposing a store's connection only returns the native
/// handle to the pool and the <c>-wal</c>/<c>-shm</c> files stay open for the
/// whole process lifetime; and nothing ever checkpoints, so the WAL that
/// survives the process holds committed data the main database file does not.
/// A reader that inherits that state has to recover the WAL before it can see
/// recent runs. Releasing the pool and truncating the WAL here leaves a
/// self-contained database file behind instead.
/// </remarks>
public static class SqliteShutdown
{
    public static void ReleaseRunLogDatabase(string? databasePath)
    {
        // Drop pooled handles first: a checkpoint cannot reclaim WAL frames that
        // idle pooled connections still hold read marks on.
        SqliteConnection.ClearAllPools();

        if (!string.IsNullOrWhiteSpace(databasePath) && File.Exists(databasePath))
            Checkpoint(databasePath!);

        // The checkpoint connection went back to the pool on dispose; clear again
        // so the process leaves no sqlite3 handle, and no -shm mapping, behind.
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
