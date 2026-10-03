#nullable enable
using BazaarPlusPlus.Storage.RunScreenshot;
using Microsoft.Data.Sqlite;

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-screenshot-sqlite-store-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = Path.Combine(tempRoot, "run-screenshots.db");

try
{
    var store = new RunScreenshotSqliteStore(dbPath);

    var localCapturedAt = new DateTimeOffset(2026, 4, 8, 21, 30, 15, TimeSpan.FromHours(8));
    var utcCapturedAt = localCapturedAt.ToUniversalTime();

    store.Save(
        new RunScreenshotRecord
        {
            ScreenshotId = "shot-primary-001",
            RunId = "run-001",
            HeroName = "Vanessa",
            IsPrimary = true,
            ImageRelativePath = Path.Combine(
                "2026-04-08",
                "2026-04-08_21-30-25-000_final_run-run-001.png"
            ),
            CapturedAtLocal = localCapturedAt.AddSeconds(10),
            CapturedAtUtc = utcCapturedAt.AddSeconds(10),
            Day = 10,
            PlayerRank = "Legendary",
            PlayerRating = 1533,
            PlayerPosition = 41,
            VictoriesAtCapture = 10,
        }
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
    var artifact = store.TryGetLatestPrimaryForRun("run-001");
    Assert(artifact != null, "The latest primary screenshot should be returned.");
    Assert(
        artifact!.ImageRelativePath
            == Path.Combine("2026-04-08", "2026-04-08_21-30-25-000_final_run-run-001.png"),
        "The screenshot query should preserve the stored relative path."
    );
    Assert(
        artifact.CapturedAtUtc == utcCapturedAt.AddSeconds(10),
        "The screenshot query should return the stored UTC instant."
    );
    Assert(
        store.TryGetLatestPrimaryForRun("missing-run") == null,
        "A run without a primary screenshot should return null."
    );
    ExpectSqliteConstraint(
        () =>
            store.Save(
                new RunScreenshotRecord
                {
                    ScreenshotId = "shot-primary-002",
                    RunId = "run-001",
                    HeroName = "Vanessa",
                    IsPrimary = true,
                    ImageRelativePath = Path.Combine("2026-04-08", "duplicate-primary.png"),
                    CapturedAtLocal = localCapturedAt.AddSeconds(30),
                    CapturedAtUtc = utcCapturedAt.AddSeconds(30),
                    Day = 10,
                    PlayerRank = "Legendary",
                    PlayerRating = 1539,
                    PlayerPosition = 39,
                    VictoriesAtCapture = 10,
                }
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
    catch (SqliteException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
