#nullable enable
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

// `user_version` names the column shape the installer reads, so a version bump must change it and a
// column change must bump it (ADR-0011).
internal static class RunLogSchemaColumnShapeTests
{
    // One fingerprint per shipped LocalDatabaseSchemaVersion. Version 3 only repaired queue data and
    // shares version 2's shape; it is the last bump without a column change, so version 2 is omitted.
    private static readonly IReadOnlyDictionary<int, string> ShippedColumnShapes = new Dictionary<
        int,
        string
    >
    {
        [3] = "b3458fe8b3a4f5c5",
    };

    internal static void Run()
    {
        CurrentVersionMatchesItsRecordedColumnShape();
        EveryShippedVersionHasADistinctColumnShape();
    }

    private static void CurrentVersionMatchesItsRecordedColumnShape()
    {
        var version = RunLogSchema.LocalDatabaseSchemaVersion;
        var actual = FreshColumnShape();
        if (!ShippedColumnShapes.TryGetValue(version, out var recorded))
            throw new InvalidOperationException(
                $"Record column shape {actual} for schema version {version}. It must differ from every earlier version's; a data repair does not bump the version (ADR-0011)."
            );
        if (!string.Equals(recorded, actual, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Schema version {version} is recorded with column shape {recorded}, but BootstrapSql creates {actual}. A column change bumps LocalDatabaseSchemaVersion, adds a migration, and records the new shape (ADR-0011)."
            );
    }

    private static void EveryShippedVersionHasADistinctColumnShape()
    {
        foreach (
            var shared in ShippedColumnShapes
                .GroupBy(entry => entry.Value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
            throw new InvalidOperationException(
                $"Schema versions {string.Join(", ", shared.Select(entry => entry.Key))} share one column shape. A data repair does not bump the version (ADR-0011)."
            );
    }

    private static string FreshColumnShape()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        RunLogSchema.EnsureInitialized(connection);

        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                tables.Add(reader.GetString(0));
        }

        var shape = new StringBuilder();
        foreach (var table in tables)
        {
            shape.Append(table).Append('(');
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // name, declared type, NOT NULL, default expression, primary-key position.
                shape
                    .Append(reader.GetString(1))
                    .Append(' ')
                    .Append(reader.GetString(2))
                    .Append(' ')
                    .Append(reader.GetInt64(3))
                    .Append(' ')
                    .Append(reader.IsDBNull(4) ? "-" : reader.GetString(4))
                    .Append(' ')
                    .Append(reader.GetInt64(5))
                    .Append(';');
            }
            shape.Append(")\n");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(shape.ToString()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
