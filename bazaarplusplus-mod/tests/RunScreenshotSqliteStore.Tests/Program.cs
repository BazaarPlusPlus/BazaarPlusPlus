#nullable enable
using System.Reflection;
using Microsoft.Data.Sqlite;

var recordType = RequireType("BazaarPlusPlus.Storage.RunScreenshot.RunScreenshotRecord");
var sourceType = RequireType("BazaarPlusPlus.Storage.RunScreenshot.RunScreenshotCaptureSource");
var storeType = RequireType("BazaarPlusPlus.Storage.RunScreenshot.RunScreenshotSqliteStore");

var ctor = storeType.GetConstructor([typeof(string)]);
Assert(
    ctor != null,
    "RunScreenshotSqliteStore should expose a constructor taking the database path."
);

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-screenshot-sqlite-store-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "run-screenshots.db");

try
{
    var store = ctor!.Invoke([dbPath]);

    var saveMethod = storeType.GetMethod("Save", BindingFlags.Public | BindingFlags.Instance);
    Assert(saveMethod != null, "RunScreenshotSqliteStore should expose Save.");

    var localCapturedAt = new DateTimeOffset(2026, 4, 8, 21, 30, 15, TimeSpan.FromHours(8));
    var utcCapturedAt = localCapturedAt.ToUniversalTime();

    saveMethod!.Invoke(
        store,
        [
            CreateRecord(
                recordType,
                sourceType,
                screenshotId: "shot-primary-001",
                runId: "run-001",
                heroName: "Vanessa",
                battleId: null,
                captureSource: "EndOfRunAuto",
                isPrimary: true,
                relativePath: Path.Combine(
                    "2026-04-08",
                    "2026-04-08_21-30-25-000_final_run-run-001.png"
                ),
                localCapturedAt.AddSeconds(10),
                utcCapturedAt.AddSeconds(10),
                day: 10,
                playerRank: "Legendary",
                playerRating: 1533,
                playerPosition: 41,
                victoriesAtCapture: 10
            ),
        ]
    );

    Assert(
        File.Exists(dbPath),
        "RunScreenshotSqliteStore should initialize the SQLite database file."
    );

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    Assert(
        CountRows(connection, "run_screenshots") == 1,
        "run_screenshots should persist the end-of-run screenshot row."
    );
    Assert(
        GetString(
            connection,
            "SELECT capture_source FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-primary-001"
        ) == "end_of_run_auto",
        "run_screenshots should serialize capture_source in snake_case."
    );
    Assert(
        GetInt64(
            connection,
            "SELECT is_primary FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-primary-001"
        ) == 1,
        "run_screenshots should mark end-of-run primary screenshots."
    );
    Assert(
        GetInt64(
            connection,
            "SELECT player_position FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-primary-001"
        ) == 41,
        "run_screenshots should persist player_position."
    );
    Assert(
        GetInt64(
            connection,
            "SELECT victories_at_capture FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-primary-001"
        ) == 10,
        "run_screenshots should persist victories_at_capture for the end-of-run screenshot."
    );
    Assert(
        GetString(
            connection,
            "SELECT hero_name FROM run_screenshots WHERE screenshot_id = $id;",
            "shot-primary-001"
        ) == "Vanessa",
        "run_screenshots should persist hero_name."
    );
    var latestMethod = storeType.GetMethod(
        "TryGetLatestPrimaryForRun",
        BindingFlags.Public | BindingFlags.Instance
    );
    Assert(
        latestMethod != null,
        "RunScreenshotSqliteStore should expose the primary screenshot query."
    );
    var artifact = latestMethod!.Invoke(store, ["run-001"]);
    Assert(artifact != null, "The latest primary screenshot should be returned.");
    var artifactType = artifact!.GetType();
    Assert(
        (string?)artifactType.GetProperty("ImageRelativePath")?.GetValue(artifact)
            == Path.Combine("2026-04-08", "2026-04-08_21-30-25-000_final_run-run-001.png"),
        "The screenshot query should preserve the stored relative path."
    );
    Assert(
        (DateTimeOffset?)artifactType.GetProperty("CapturedAtUtc")?.GetValue(artifact)
            == utcCapturedAt.AddSeconds(10),
        "The screenshot query should return the stored UTC instant."
    );
    Assert(
        latestMethod.Invoke(store, ["missing-run"]) == null,
        "A run without a primary screenshot should return null."
    );
    ExpectSqliteConstraint(
        () =>
            saveMethod.Invoke(
                store,
                [
                    CreateRecord(
                        recordType,
                        sourceType,
                        screenshotId: "shot-primary-002",
                        runId: "run-001",
                        heroName: "Vanessa",
                        battleId: null,
                        captureSource: "EndOfRunAuto",
                        isPrimary: true,
                        relativePath: Path.Combine("2026-04-08", "duplicate-primary.png"),
                        localCapturedAt.AddSeconds(30),
                        utcCapturedAt.AddSeconds(30),
                        day: 10,
                        playerRank: "Legendary",
                        playerRating: 1539,
                        playerPosition: 39,
                        victoriesAtCapture: 10
                    ),
                ]
            ),
        "only one primary screenshot should exist per run."
    );
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch { }
}

Console.WriteLine("Run screenshot SQLite store checks passed.");

static object CreateRecord(
    Type recordType,
    Type sourceType,
    string screenshotId,
    string? runId,
    string? heroName,
    string? battleId,
    string captureSource,
    bool isPrimary,
    string relativePath,
    DateTimeOffset localCapturedAt,
    DateTimeOffset utcCapturedAt,
    int? day,
    string? playerRank,
    int? playerRating,
    int? playerPosition,
    int? victoriesAtCapture
)
{
    var record =
        Activator.CreateInstance(recordType)
        ?? throw new InvalidOperationException("RunScreenshotRecord should be constructible.");

    SetProperty(recordType, record, "ScreenshotId", screenshotId);
    SetProperty(recordType, record, "RunId", runId);
    SetProperty(recordType, record, "HeroName", heroName);
    SetProperty(recordType, record, "BattleId", battleId);
    SetProperty(recordType, record, "CaptureSource", Enum.Parse(sourceType, captureSource));
    SetProperty(recordType, record, "IsPrimary", isPrimary);
    SetProperty(recordType, record, "ImageRelativePath", relativePath);
    SetProperty(recordType, record, "CapturedAtLocal", localCapturedAt);
    SetProperty(recordType, record, "CapturedAtUtc", utcCapturedAt);
    SetProperty(recordType, record, "Day", day);
    SetProperty(recordType, record, "PlayerRank", playerRank);
    SetProperty(recordType, record, "PlayerRating", playerRating);
    SetProperty(recordType, record, "PlayerPosition", playerPosition);
    SetProperty(recordType, record, "VictoriesAtCapture", victoriesAtCapture);
    return record;
}

static void SetProperty(Type type, object instance, string name, object? value)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    property.SetValue(instance, value);
}

static long CountRows(SqliteConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
    return (long)(command.ExecuteScalar() ?? 0L);
}

static string GetString(SqliteConnection connection, string sql, string id)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", id);
    return (string)(command.ExecuteScalar() ?? throw new InvalidOperationException(sql));
}

static long GetInt64(SqliteConnection connection, string sql, string id)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", id);
    return (long)(command.ExecuteScalar() ?? throw new InvalidOperationException(sql));
}

static void ExpectSqliteConstraint(Action action, string message)
{
    try
    {
        action();
    }
    catch (TargetInvocationException ex) when (ex.InnerException is SqliteException)
    {
        return;
    }
    catch (SqliteException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus.Storage")
        ?? Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
