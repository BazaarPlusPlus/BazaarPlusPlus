#nullable enable
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

internal static class RunLogSchemaIndexTests
{
    // Must stay byte-identical to the runId == null branch of RunLogStore.TryReadActiveRun.
    private const string ActiveRunSelect = """
        SELECT *
        FROM runs
        WHERE completed = 0
        ORDER BY last_seen_at_utc DESC
        LIMIT 1;
        """;

    private const string OldIndexName = "idx_runs_status_last_seen";
    private const string NewIndexName = "idx_runs_completed_last_seen";

    internal static void Run()
    {
        ActiveRunLookupSearchesTheCompletedIndex();
        OpeningAnOlderDatabaseReplacesTheStatusIndex();
    }

    private static void ActiveRunLookupSearchesTheCompletedIndex()
    {
        WithDatabase(connection =>
        {
            RunLogSchema.EnsureInitialized(connection);

            Equal(1L, IndexCount(connection, NewIndexName), "new index exists");
            Equal(0L, IndexCount(connection, OldIndexName), "old index removed");

            var plan = QueryPlan(connection, ActiveRunSelect);
            if (!plan.Contains(NewIndexName, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"TryReadActiveRun plan does not use {NewIndexName}: {plan}"
                );
            if (!plan.Contains("SEARCH", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"TryReadActiveRun plan is not a SEARCH: {plan}"
                );
            if (plan.Contains("SCAN", StringComparison.Ordinal))
                throw new InvalidOperationException($"TryReadActiveRun plan still scans: {plan}");
            if (plan.Contains("TEMP B-TREE", StringComparison.Ordinal))
                throw new InvalidOperationException($"TryReadActiveRun plan still sorts: {plan}");
        });
    }

    private static void OpeningAnOlderDatabaseReplacesTheStatusIndex()
    {
        WithDatabase(connection =>
        {
            // A database already at the current version, but carrying the previously shipped index.
            RunLogSchema.EnsureInitialized(connection);
            Execute(
                connection,
                $"""
                DROP INDEX IF EXISTS {NewIndexName};
                CREATE INDEX IF NOT EXISTS {OldIndexName}
                    ON runs(status, last_seen_at_utc DESC);
                INSERT INTO runs (
                    run_id, started_at_utc, last_seen_at_utc, status, completed, hero, game_mode
                ) VALUES (
                    'legacy-run', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z',
                    'active', 0, 'Vanessa', 'Ranked'
                );
                """
            );
            Equal(1L, IndexCount(connection, OldIndexName), "seeded old index");

            RunLogSchema.EnsureInitialized(connection);

            Equal(0L, IndexCount(connection, OldIndexName), "old index dropped on open");
            Equal(1L, IndexCount(connection, NewIndexName), "new index created on open");
            Equal(2L, Scalar(connection, "PRAGMA user_version;"), "schema version unchanged");
            Equal(1L, Scalar(connection, "SELECT COUNT(*) FROM runs;"), "existing rows preserved");
        });
    }

    private static string QueryPlan(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        using var reader = command.ExecuteReader();
        var lines = new List<string>();
        while (reader.Read())
            lines.Add(reader.GetString(reader.GetOrdinal("detail")));
        return string.Join(" | ", lines);
    }

    private static long IndexCount(SqliteConnection connection, string indexName) =>
        Scalar(
            connection,
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{indexName}';"
        );

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
