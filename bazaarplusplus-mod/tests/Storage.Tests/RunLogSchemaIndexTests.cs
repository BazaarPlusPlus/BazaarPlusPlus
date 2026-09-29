#nullable enable
using BazaarPlusPlus.Storage.BundleQueue;
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

    // A History page names this index in INDEXED BY, which errors when the index cannot serve it.
    private const string HintedIndexName = "idx_battles_history_ghost";

    internal static void Run()
    {
        ActiveRunLookupSearchesTheCompletedIndex();
        OpeningAnOlderDatabaseReplacesTheStatusIndex();
        OpeningADatabaseRedefinesADriftedIndex();
        ReopeningAnUpToDateDatabaseLeavesTheSchemaAlone();
        SealEligibilitySeeksTheOutboxByRun();
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

    private static void SealEligibilitySeeksTheOutboxByRun()
    {
        WithDatabase(connection =>
        {
            RunLogSchema.EnsureInitialized(connection);
            var plan = QueryPlan(
                connection,
                $"SELECT run_id FROM runs AS r WHERE {BundleQueueStore.SealEligibleRunCondition("r")};"
            );
            if (
                plan.Contains("SCAN r_outbox", StringComparison.Ordinal)
                || !plan.Contains(
                    "SEARCH r_outbox USING COVERING INDEX idx_bundle_outbox_run",
                    StringComparison.Ordinal
                )
            )
                throw new InvalidOperationException(
                    $"Seal eligibility must seek bundle_outbox by run: {plan}"
                );
        });
    }

    private static bool TryGhostHistoryRead(SqliteConnection connection)
    {
        try
        {
            Scalar(
                connection,
                $"SELECT COUNT(*) FROM battles INDEXED BY {HintedIndexName} WHERE source = 'GHOST' AND deleted_at_utc IS NULL AND local_player_account_id = 'account';"
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
            Equal(3L, Scalar(connection, "PRAGMA user_version;"), "schema version unchanged");
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
