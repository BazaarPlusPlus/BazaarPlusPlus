#nullable enable
using System.Reflection;
using BazaarPlusPlus.Storage.Paths;
using BazaarPlusPlus.Storage.RunLog;
using Microsoft.Data.Sqlite;

var storeType = RequireType("BazaarPlusPlus.Storage.RunLog.RunLogStore");
var ctor = storeType.GetConstructor([typeof(IPathProvider)]);
Assert(ctor != null, "RunLogStore should expose a constructor taking IPathProvider.");

var tempRoot = Path.Combine(
    Path.GetTempPath(),
    "bpp-run-log-sqlite-store-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = PathConstants.RunLogDatabase(tempRoot);

try
{
    var paths = new TempPathProvider(tempRoot);
    var store = ctor!.Invoke([paths]);
    var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
    const string runId = "run_20260315t121530z_vanessa_ranked_002a_deadbeef";

    var sessionState = Invoke<RunLogSessionState>(
        storeType,
        store,
        "CreateRun",
        [
            new RunLogCreateRequest
            {
                RunId = runId,
                StartedAtUtc = startedAt,
                Hero = "Vanessa",
                GameMode = "Ranked",
                PlayerRank = "Gold 2",
                PlayerRating = 1420,
                Day = 1,
                Hour = 1,
                Seed = 42,
            },
        ]
    );

    Assert(sessionState.RunId == runId, "CreateRun should return the created run state.");

    InvokeVoid(
        storeType,
        store,
        "AppendEvent",
        [
            runId,
            new RunLogEvent
            {
                RunId = runId,
                Seq = 1,
                Ts = startedAt,
                Kind = "run_started",
                Day = 1,
                Hour = 1,
                Hero = "Vanessa",
                GameMode = "Ranked",
            },
        ]
    );
    InvokeVoid(
        storeType,
        store,
        "AppendEvent",
        [
            runId,
            new RunLogEvent
            {
                RunId = runId,
                Seq = 2,
                Ts = startedAt.AddSeconds(5),
                Kind = "pvp_combat_recorded",
                Day = 1,
                Hour = 2,
                BattleId = "battle-001",
                OpponentName = "Test Rival",
            },
        ]
    );

    InvokeVoid(
        storeType,
        store,
        "SaveCheckpoint",
        [
            runId,
            new RunLogCheckpoint
            {
                RunId = runId,
                LastSeq = 2,
                LastSeenAtUtc = startedAt.AddSeconds(5),
                Day = 1,
                Hour = 2,
                Completed = false,
            },
        ]
    );

    InvokeVoid(
        storeType,
        store,
        "CompleteRun",
        [
            runId,
            new RunLogCompletion
            {
                RunId = runId,
                Status = "completed",
                EndedAtUtc = startedAt.AddMinutes(15),
                FinalDay = 3,
                FinalHour = 1,
                Victories = 10,
                Losses = 1,
                FinalPlayerRank = "Legendary",
                FinalPlayerRating = 1436,
                Reason = "run_end_event",
            },
        ]
    );

    Assert(File.Exists(dbPath), "CreateRun should initialize the SQLite database file.");

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();

        Assert(CountRows(connection, "runs") == 1, "runs should contain exactly one row.");
        Assert(
            GetString(connection, "SELECT run_id FROM runs WHERE run_id = $runId;", runId) == runId,
            "runs should contain the created run."
        );
        Assert(
            GetString(connection, "SELECT player_rank FROM runs WHERE run_id = $runId;", runId)
                == "Gold 2",
            "runs should persist the player's rank snapshot."
        );
        Assert(
            GetInt64(connection, "SELECT player_rating FROM runs WHERE run_id = $runId;", runId)
                == 1420,
            "runs should persist the player's rating snapshot."
        );
        Assert(
            GetInt64(connection, "SELECT last_seq FROM runs WHERE run_id = $runId;", runId) == 2,
            "runs should inline the latest observed sequence."
        );
        Assert(
            GetInt64(connection, "SELECT completed FROM runs WHERE run_id = $runId;", runId) == 1,
            "runs should mark terminal runs as completed."
        );
        Assert(
            GetString(connection, "SELECT status FROM runs WHERE run_id = $runId;", runId)
                == "completed",
            "runs should persist terminal status."
        );
        Assert(
            GetString(
                connection,
                "SELECT final_player_rank FROM runs WHERE run_id = $runId;",
                runId
            ) == "Legendary",
            "runs should persist the final player rank snapshot."
        );
        Assert(
            GetInt64(
                connection,
                "SELECT final_player_rating FROM runs WHERE run_id = $runId;",
                runId
            ) == 1436,
            "runs should persist the final player rating snapshot."
        );
        Assert(
            GetInt64(
                connection,
                "SELECT final_player_rating_delta FROM runs WHERE run_id = $runId;",
                runId
            ) == 16,
            "runs should persist the final player rating delta."
        );

        const string abandonedRunId = "server-run-456";
        Invoke<RunLogSessionState>(
            storeType,
            store,
            "CreateRun",
            [
                new RunLogCreateRequest
                {
                    RunId = abandonedRunId,
                    StartedAtUtc = startedAt.AddHours(1),
                    Hero = "Pygmalien",
                    GameMode = "Unranked",
                    Day = 1,
                    Hour = 1,
                },
            ]
        );
        InvokeVoid(
            storeType,
            store,
            "CompleteRun",
            [
                abandonedRunId,
                new RunLogCompletion
                {
                    RunId = abandonedRunId,
                    Status = "abandoned",
                    EndedAtUtc = startedAt.AddHours(1).AddMinutes(5),
                    FinalDay = 1,
                    FinalHour = 2,
                    Reason = "interrupted",
                },
            ]
        );
        Assert(
            GetString(connection, "SELECT status FROM runs WHERE run_id = $runId;", abandonedRunId)
                == "abandoned",
            "runs should preserve interrupted/abandoned terminal statuses."
        );

        var collisionSession = Invoke<RunLogSessionState>(
            storeType,
            store,
            "CreateRun",
            [
                new RunLogCreateRequest
                {
                    RunId = abandonedRunId,
                    StartedAtUtc = startedAt.AddHours(1).AddMinutes(10),
                    Hero = "Dooley",
                    GameMode = "Unranked",
                    Day = 1,
                    Hour = 1,
                },
            ]
        );
        Assert(
            collisionSession.RunId != abandonedRunId
                && RunLogRunIdentity.MatchesServerRunId(collisionSession.RunId, abandonedRunId),
            "A reused terminal server run id should create a distinct matching local run id."
        );
        Assert(
            GetString(connection, "SELECT status FROM runs WHERE run_id = $runId;", abandonedRunId)
                == "abandoned",
            "Recovering a reused server run id must not rewrite the historical terminal row."
        );
        Assert(
            GetString(
                connection,
                "SELECT status FROM runs WHERE run_id = $runId;",
                collisionSession.RunId
            ) == "active",
            "The recovered collision row should be independently active."
        );

        const string olderActiveRunId = "server-run-older";
        const string newerActiveRunId = "server-run-newer";
        var olderStartedAt = startedAt.AddHours(2);
        var newerStartedAt = startedAt.AddHours(3);

        Invoke<RunLogSessionState>(
            storeType,
            store,
            "CreateRun",
            [
                new RunLogCreateRequest
                {
                    RunId = olderActiveRunId,
                    StartedAtUtc = olderStartedAt,
                    Hero = "Vanessa",
                    GameMode = "Ranked",
                    PlayerRank = "Silver 3",
                    PlayerRating = 1200,
                    Day = 2,
                    Hour = 1,
                },
            ]
        );
        InvokeVoid(
            storeType,
            store,
            "SaveCheckpoint",
            [
                olderActiveRunId,
                new RunLogCheckpoint
                {
                    RunId = olderActiveRunId,
                    LastSeq = 4,
                    LastSeenAtUtc = olderStartedAt.AddMinutes(5),
                    Day = 2,
                    Hour = 4,
                    Completed = false,
                },
            ]
        );

        Invoke<RunLogSessionState>(
            storeType,
            store,
            "CreateRun",
            [
                new RunLogCreateRequest
                {
                    RunId = newerActiveRunId,
                    StartedAtUtc = newerStartedAt,
                    Hero = "Dooley",
                    GameMode = "Ranked",
                    Day = 3,
                    Hour = 1,
                },
            ]
        );
        InvokeVoid(
            storeType,
            store,
            "SaveCheckpoint",
            [
                newerActiveRunId,
                new RunLogCheckpoint
                {
                    RunId = newerActiveRunId,
                    LastSeq = 2,
                    LastSeenAtUtc = newerStartedAt.AddMinutes(10),
                    Day = 3,
                    Hour = 2,
                    Completed = false,
                },
            ]
        );

        var resumedOlderSession = Invoke<RunLogSessionState>(
            storeType,
            store,
            "CreateRun",
            [
                new RunLogCreateRequest
                {
                    RunId = olderActiveRunId,
                    StartedAtUtc = olderStartedAt.AddMinutes(30),
                    Hero = "Vanessa",
                    GameMode = "Ranked",
                    PlayerRank = "Gold 1",
                    PlayerRating = 1333,
                    Day = 2,
                    Hour = 5,
                },
            ]
        );

        Assert(
            resumedOlderSession.RunId == olderActiveRunId,
            "CreateRun should resume an existing active run with the requested run id."
        );
        Assert(
            resumedOlderSession.LastSeq == 4,
            "CreateRun should preserve the existing checkpoint sequence when resuming."
        );
        Assert(
            resumedOlderSession.Hour == 4,
            "CreateRun should preserve the persisted checkpoint hour when resuming."
        );
        Assert(
            GetInt64(
                connection,
                "SELECT COUNT(*) FROM runs WHERE run_id = $runId;",
                olderActiveRunId
            ) == 1,
            "CreateRun should not duplicate an existing active run row."
        );
        Assert(
            GetString(
                connection,
                "SELECT player_rank FROM runs WHERE run_id = $runId;",
                olderActiveRunId
            ) == "Gold 1",
            "CreateRun should refresh the stored player rank snapshot on conflict."
        );
        Assert(
            GetInt64(
                connection,
                "SELECT player_rating FROM runs WHERE run_id = $runId;",
                olderActiveRunId
            ) == 1333,
            "CreateRun should refresh the stored player rating snapshot on conflict."
        );
    }
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("RunLogging SQLite store checks passed.");

static Type RequireType(string fullName)
{
    return Type.GetType($"{fullName}, BazaarPlusPlus.Storage")
        ?? Type.GetType($"{fullName}, BazaarPlusPlus")
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static T Invoke<T>(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    return (T)method.Invoke(instance, args)!;
}

static void InvokeVoid(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    method.Invoke(instance, args);
}

static long CountRows(SqliteConnection connection, string tableName)
{
    using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
    return (long)(command.ExecuteScalar() ?? 0L);
}

static long GetInt64(SqliteConnection connection, string sql, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$runId", runId);
    return (long)(
        command.ExecuteScalar()
        ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
}

static string GetString(SqliteConnection connection, string sql, string runId)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$runId", runId);
    return (string)(
        command.ExecuteScalar()
        ?? throw new InvalidOperationException($"Query returned null: {sql}")
    );
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class TempPathProvider : IPathProvider
{
    public TempPathProvider(string dataRoot) => DataRootDirectoryPath = dataRoot;

    public string? DataRootDirectoryPath { get; }
    public string? PluginsDirectoryPath => null;
}
