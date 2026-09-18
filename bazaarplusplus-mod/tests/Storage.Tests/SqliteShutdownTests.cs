#nullable enable
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.Sqlite;
using Microsoft.Data.Sqlite;

internal static class SqliteShutdownTests
{
    public static void Run()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "bpp-storage-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);
        var dbPath = PathConstants.RunLogDatabase(tempRoot);
        var walPath = dbPath + "-wal";

        try
        {
            var store = new RunLogStore(new TempDirPathProvider(tempRoot));
            const string runId = "run_20260301t100000z_test_ranked_002_deadbeef";
            var startedAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
            store.CreateRun(
                new RunLogCreateRequest
                {
                    RunId = runId,
                    StartedAtUtc = startedAt,
                    Hero = "Test",
                    GameMode = "Ranked",
                    Day = 1,
                    Hour = 1,
                    Seed = 7,
                }
            );

            // The committed run lives in the WAL, not the main database file: this is
            // the state an unmanaged process exit strands for the installer to read.
            Assert(
                File.Exists(walPath) && new FileInfo(walPath).Length > 0,
                "Writes should leave a non-empty WAL before shutdown."
            );

            SqliteShutdown.ReleaseRunLogDatabase(dbPath);

            Assert(
                !File.Exists(walPath) || new FileInfo(walPath).Length == 0,
                "Shutdown should truncate or remove the WAL."
            );
            Assert(
                ReadRunCount(dbPath) == 1,
                "A read-only reader should see the run without recovering a WAL."
            );

            Console.WriteLine("SqliteShutdownTests passed.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static long ReadRunCount(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {RunLogSchema.RunsTableName};";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
