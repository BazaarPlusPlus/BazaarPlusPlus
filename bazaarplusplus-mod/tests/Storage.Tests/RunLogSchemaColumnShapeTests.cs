#nullable enable
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class RunLogSchemaColumnShapeTests
{
    private static readonly IReadOnlyDictionary<int, string> ShippedColumnShapes = new Dictionary<
        int,
        string
    >
    {
        [1] = "6b01d7570e831f1f",
    };

    [Fact]
    public void CurrentVersionMatchesItsRecordedColumnShape()
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

    [Fact]
    public void EveryShippedVersionHasADistinctColumnShape()
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
                // PRAGMA table_info columns here are name, declared type, NOT NULL, default, and primary-key position.
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
