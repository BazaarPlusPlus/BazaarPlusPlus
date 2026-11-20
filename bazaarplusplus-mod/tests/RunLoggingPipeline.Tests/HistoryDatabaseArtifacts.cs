#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.TestSupport;
using Microsoft.Data.Sqlite;

// Collision IDs contain random GUIDs, so the goldens alias them in first-seen order.
// Timestamps stay raw to catch writes that bypass the test clock.
internal sealed partial class HistoryDatabaseArtifacts
{
    private const string SchemaFile = "src/BazaarPlusPlus.Storage/history-database-v6.schema.sql";
    private const string RowsFile =
        "tests/RunLoggingPipeline.Tests/fixtures/history-database-v6.rows.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly (string Table, string OrderBy)[] Tables =
    [
        (RunLogSchema.RunsTableName, "run_id"),
        (RunLogSchema.RunEventsTableName, "run_id, seq"),
        (RunLogSchema.BattlesTableName, "battle_id"),
    ];

    private readonly string _directory;
    private readonly JsonArray _steps = new();
    private readonly Dictionary<string, string> _collisionAliases = new(StringComparer.Ordinal);

    internal HistoryDatabaseArtifacts()
    {
        _directory = Path.Combine(
            TestInputs.RepoRoot,
            "artifacts",
            "run-logging-pipeline",
            Binding
        );
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        Directory.CreateDirectory(_directory);
    }

    internal bool Failed { get; private set; }

    private static string Binding => typeof(HistoryDatabaseArtifacts).Assembly.GetName().Name!;

    internal void RecordSchema(string database)
    {
        using var connection = Open(database);
        var userVersion = Convert.ToInt32(
            Scalar(connection, "PRAGMA user_version;"),
            CultureInfo.InvariantCulture
        );
        using var mirror = JsonDocument.Parse(
            TestInputs.Fixture("src/BazaarPlusPlus.Storage/BazaarPlusPlus.history-database.json")
        );
        var mirrored = mirror.RootElement.GetProperty("historyDatabaseUserVersion").GetInt32();
        Check.That(
            userVersion == RunLogSchema.LocalDatabaseSchemaVersion && userVersion == mirrored,
            $"PRAGMA user_version {userVersion} must equal RunLogSchema.LocalDatabaseSchemaVersion "
                + $"{RunLogSchema.LocalDatabaseSchemaVersion} and BazaarPlusPlus.history-database.json {mirrored}."
        );

        var schema = new StringBuilder($"PRAGMA user_version={userVersion};\n");
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sql FROM sqlite_master
            WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%'
            ORDER BY CASE type WHEN 'table' THEN 0 WHEN 'index' THEN 1 WHEN 'trigger' THEN 2 ELSE 3 END,
                name;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            schema.Append(reader.GetString(0).Replace("\r\n", "\n")).Append(";\n");
        Compare(SchemaFile, schema.ToString());
    }

    internal void RecordStep(int number, string name, string database)
    {
        using var connection = Open(database);
        var step = new JsonObject { ["step"] = number, ["name"] = name };
        foreach (var (table, orderBy) in Tables)
            step[table] = Rows(connection, $"SELECT * FROM {table} ORDER BY {orderBy};");
        _steps.Add(step);
    }

    internal void CompareRows(bool scenarioCompleted)
    {
        var actual = StepsJson(_steps);
        if (scenarioCompleted)
        {
            Compare(RowsFile, actual);
            return;
        }
        File.WriteAllText(
            Path.Combine(_directory, Path.GetFileName(RowsFile)),
            actual,
            new UTF8Encoding(false)
        );
        var golden = JsonNode.Parse(TestInputs.Fixture(RowsFile))!;
        var reached = new JsonArray(
            golden["steps"]!
                .AsArray()
                .Take(_steps.Count)
                .Select(step => step!.DeepClone())
                .ToArray()
        );
        var expected = StepsJson(reached);
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;
        Console.Error.WriteLine($"Steps 1-{_steps.Count} differ from {RowsFile}:");
        Console.Error.WriteLine(UnifiedDiff.Render(expected, actual, RowsFile));
        Failed = true;
    }

    private static string StepsJson(JsonArray steps) =>
        new JsonObject { ["steps"] = steps.DeepClone() }.ToJsonString(JsonOptions) + "\n";

    internal static long Count(string database, string sql)
    {
        using var connection = Open(database);
        return Convert.ToInt64(Scalar(connection, sql), CultureInfo.InvariantCulture);
    }

    internal static string? Text(string database, string sql)
    {
        using var connection = Open(database);
        return Scalar(connection, sql) as string;
    }

    private void Compare(string file, string actual)
    {
        actual = actual.Replace("\r\n", "\n");
        var artifact = Path.GetFileName(file);
        File.WriteAllText(Path.Combine(_directory, artifact), actual, new UTF8Encoding(false));
        var goldenPath = Path.Combine(TestInputs.RepoRoot, file);
        if (Environment.GetEnvironmentVariable("BPP_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
            Console.WriteLine($"Rewrote {file}; review it with git diff.");
            return;
        }
        if (!File.Exists(goldenPath))
        {
            Console.Error.WriteLine($"{file} is missing; create it with BPP_UPDATE_GOLDENS=1.");
            Failed = true;
            return;
        }
        var expected = TestInputs.Fixture(file).Replace("\r\n", "\n");
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;
        Console.Error.WriteLine(
            $"Run logging output differs from {file}; actual output is "
                + $"artifacts/run-logging-pipeline/{Binding}/{artifact}. Regenerate with BPP_UPDATE_GOLDENS=1 "
                + "only if the change is intended."
        );
        Console.Error.WriteLine(UnifiedDiff.Render(expected, actual, file));
        Failed = true;
    }

    private JsonArray Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new JsonArray();
        while (reader.Read())
        {
            var row = new JsonObject();
            for (var column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = reader.IsDBNull(column)
                    ? null
                    : reader.GetValue(column) switch
                    {
                        long number => number,
                        double real => real,
                        string text => NormalizeCollisionIds(text),
                        var other => throw new InvalidOperationException(
                            $"Unexpected SQLite value {other.GetType()} in {reader.GetName(column)}."
                        ),
                    };
            }
            rows.Add(row);
        }
        return rows;
    }

    private string NormalizeCollisionIds(string text) =>
        CollisionSuffix()
            .Replace(
                text,
                match =>
                {
                    if (!_collisionAliases.TryGetValue(match.Value, out var alias))
                    {
                        alias = $":bpp:#{_collisionAliases.Count + 1}";
                        _collisionAliases.Add(match.Value, alias);
                    }
                    return alias;
                }
            );

    private static SqliteConnection Open(string database)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Pooling = false,
            }.ConnectionString
        );
        connection.Open();
        return connection;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [GeneratedRegex(":bpp:[0-9a-f]{32}")]
    private static partial Regex CollisionSuffix();
}

internal static class Check
{
    internal static void That(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
