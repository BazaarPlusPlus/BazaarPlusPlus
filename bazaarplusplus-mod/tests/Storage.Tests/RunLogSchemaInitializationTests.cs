#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class RunLogSchemaInitializationTests
{
    [Fact]
    public async Task CreatesFreshCurrentVersionAndSupportsConcurrentInitialization()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-schema-fresh-{Guid.NewGuid():N}.db"
        );
        try
        {
            await Task.WhenAll(
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
            Equal(1L, Scalar(verify, "PRAGMA user_version;"), "fresh schema version");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public void RejectsUnknownFutureVersionWithoutMutation()
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
    [Fact]
    public void RejectsAV5DatabaseWithoutMutation()
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
            Xunit.Assert.Fail($"Expected {label} to be '{expected}', got '{actual}'.");
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

        Xunit.Assert.Fail($"Expected {label} to throw {typeof(T).Name}.");
    }
}
