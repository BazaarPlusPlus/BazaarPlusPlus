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
    "bpp-run-log-sqlite-recovery-tests",
    Guid.NewGuid().ToString("N")
);
Directory.CreateDirectory(tempRoot);
var dbPath = PathConstants.RunLogDatabase(tempRoot);

try
{
    var startedAt = new DateTimeOffset(2026, 3, 15, 12, 15, 30, TimeSpan.Zero);
    const string runId = "run_20260315t121530z_vanessa_ranked_002a_deadbeef";

    var paths = new TempPathProvider(tempRoot);
    var firstStore = ctor!.Invoke([paths]);
    Invoke<RunLogSessionState>(
        storeType,
        firstStore,
        "CreateRun",
        [
            new RunLogCreateRequest
            {
                SchemaVersion = 1,
                RunId = runId,
                StartedAtUtc = startedAt,
                Hero = "Vanessa",
                GameMode = "Ranked",
                Day = 1,
                Hour = 1,
                Seed = 42,
            },
        ]
    );

    InvokeVoid(
        storeType,
        firstStore,
        "AppendEvent",
        [
            runId,
            new RunLogEvent
            {
                SchemaVersion = 1,
                RunId = runId,
                Seq = 1,
                Ts = startedAt,
                Kind = "run_started",
                Day = 1,
                Hour = 1,
            },
        ]
    );

    InvokeVoid(
        storeType,
        firstStore,
        "SaveCheckpoint",
        [
            runId,
            new RunLogCheckpoint
            {
                SchemaVersion = 1,
                RunId = runId,
                LastSeq = 1,
                LastSeenAtUtc = startedAt.AddSeconds(10),
                Day = 2,
                Hour = 1,
                Completed = false,
            },
        ]
    );

    var resumedStore = ctor.Invoke([paths]);
    var resumed = Invoke<RunLogSessionState?>(storeType, resumedStore, "TryResumeActiveRun", []);

    Assert(resumed != null, "TryResumeActiveRun should restore an unfinished run.");
    Assert(resumed!.RunId == runId, "Recovered session should preserve run_id.");
    Assert(resumed.LastSeq == 1, "Recovered session should preserve last_seq.");
    Assert(resumed.Day == 2, "Recovered session should preserve checkpoint day.");
    Assert(resumed.Hour == 1, "Recovered session should preserve checkpoint hour.");

    InvokeVoid(
        storeType,
        resumedStore,
        "MarkRunAbandoned",
        [
            runId,
            new RunLogAbandonment
            {
                SchemaVersion = 1,
                RunId = runId,
                Status = "abandoned",
                EndedAtUtc = startedAt.AddMinutes(20),
                FinalDay = 2,
                FinalHour = 1,
                Reason = "process_exit",
            },
        ]
    );

    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
    {
        connection.Open();
        Assert(
            GetString(connection, "SELECT status FROM runs WHERE run_id = $runId;", runId)
                == "abandoned",
            "runs should mark the run abandoned."
        );
    }

    var resumedAfterAbandonment = Invoke<RunLogSessionState?>(
        storeType,
        resumedStore,
        "TryResumeActiveRun",
        []
    );
    Assert(
        resumedAfterAbandonment == null,
        "Abandoned runs should no longer be returned by TryResumeActiveRun."
    );
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempRoot))
        Directory.Delete(tempRoot, recursive: true);
}

Console.WriteLine("RunLogging SQLite recovery checks passed.");

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
