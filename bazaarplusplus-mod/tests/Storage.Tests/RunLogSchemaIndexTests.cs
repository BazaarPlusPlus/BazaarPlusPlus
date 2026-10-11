#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.TestSupport;
using Microsoft.Data.Sqlite;

internal static class RunLogSchemaIndexTests
{
    // A History page names this index in INDEXED BY, which errors when the index cannot serve it.
    private const string HintedIndexName = "idx_battles_history_ghost";

    internal static void Run()
    {
        OpeningTheShippedSchemaFileDropsNothing();
        OpeningADatabaseRedefinesADriftedIndex();
        ReopeningAnUpToDateDatabaseLeavesTheSchemaAlone();
    }

    private static void OpeningADatabaseRedefinesADriftedIndex()
    {
        var expected = new Dictionary<string, string>();
        WithDatabase(connection =>
        {
            RunLogSchema.EnsureInitialized(connection);
            foreach (var (name, sql) in IndexDefinitions(connection))
                expected[name] = sql;
        });

        WithDatabase(connection =>
        {
            // An older release that shipped a different definition under the same name.
            RunLogSchema.EnsureInitialized(connection);
            Execute(
                connection,
                $"DROP INDEX {HintedIndexName}; CREATE INDEX {HintedIndexName} ON battles(local_player_account_id) WHERE source = 'GHOST' AND day >= 10;"
            );
            if (TryGhostHistoryRead(connection))
                throw new InvalidOperationException(
                    "The drifted index must be unusable for the hinted Ghost read."
                );

            RunLogSchema.EnsureInitialized(connection);

            var actual = IndexDefinitions(connection);
            Equal(expected.Count, actual.Count, "index count after repair");
            foreach (var (name, sql) in actual)
                Equal(expected[name], sql, $"definition of {name}");
            if (!TryGhostHistoryRead(connection))
                throw new InvalidOperationException(
                    "The hinted Ghost read must succeed after the index is repaired."
                );
        });
    }

    private static void ReopeningAnUpToDateDatabaseLeavesTheSchemaAlone()
    {
        WithDatabase(connection =>
        {
            RunLogSchema.EnsureInitialized(connection);
            var before = Scalar(connection, "PRAGMA schema_version;");
            RunLogSchema.EnsureInitialized(connection);
            Equal(before, Scalar(connection, "PRAGMA schema_version;"), "schema cookie on reopen");
        });
    }

    private static bool TryGhostHistoryRead(SqliteConnection connection)
    {
        try
        {
            Scalar(
                connection,
                $"SELECT COUNT(*) FROM battles INDEXED BY {HintedIndexName} WHERE source = 'GHOST' AND deleted_at_ms IS NULL AND local_player_account_id = 'account';"
            );
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static List<(string Name, string Sql)> IndexDefinitions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL ORDER BY name;";
        using var reader = command.ExecuteReader();
        var rows = new List<(string, string)>();
        while (reader.Read())
            rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    // The schema file the installer and import build against is exactly what BootstrapSql creates,
    // so a database built from it opens with no index rebuilt and no DDL run.
    private static void OpeningTheShippedSchemaFileDropsNothing()
    {
        WithDatabase(connection =>
        {
            Execute(
                connection,
                TestInputs.Fixture("src/BazaarPlusPlus.Storage/history-database-v6.schema.sql")
            );
            var indexes = IndexDefinitions(connection);
            var before = Scalar(connection, "PRAGMA schema_version;");

            RunLogSchema.EnsureInitialized(connection);

            Equal(before, Scalar(connection, "PRAGMA schema_version;"), "schema cookie on open");
            var after = IndexDefinitions(connection);
            Equal(indexes.Count, after.Count, "index count on open");
            for (var index = 0; index < after.Count; index++)
                Equal(indexes[index], after[index], $"index {after[index].Name} on open");
            Equal(
                (long)RunLogSchema.LocalDatabaseSchemaVersion,
                Scalar(connection, "PRAGMA user_version;"),
                "schema file version"
            );
        });
    }

    private static void WithDatabase(Action<SqliteConnection> body)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"bpp-schema-index-{Guid.NewGuid():N}.db"
        );
        try
        {
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                body(connection);
            }
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
}
