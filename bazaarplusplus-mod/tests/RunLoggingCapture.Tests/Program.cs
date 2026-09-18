#nullable enable
using System.Reflection;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Storage.RunLog;

var basicsType = RequireType("BazaarPlusPlus.Core.GameState.RunBasicsSnapshot");
var mapperType = RequireType("BazaarPlusPlus.Game.RunLogging.RunLogRecordMapper");
var runExitKindType = RequireType("BazaarPlusPlus.Core.RunContext.RunExitKind");
var basics = Activator.CreateInstance(basicsType)!;
SetProperty(basicsType, basics, "Day", 8);
SetProperty(basicsType, basics, "Hour", 3);
SetProperty(basicsType, basics, "Victories", 10);
SetProperty(basicsType, basics, "Losses", 2);
SetProperty(basicsType, basics, "Hero", "Pygmalien");
SetProperty(basicsType, basics, "GameMode", "Ranked");
var rank = new RankSnapshot { Rank = "Diamond", Rating = 2410 };

var createArgs = new object?[] { basics, rank, "server-run-42", "Ptr", null };
var created = Invoke<bool>(mapperType, null, "TryCreateRunLogCreateRequest", createArgs);
var createRequest = (RunLogCreateRequest?)createArgs[4];
Assert(created && createRequest != null, "Valid run basics should produce a create request.");
Assert(
    createRequest!.RunId == "server-run-42"
        && createRequest.Hero == "Pygmalien"
        && createRequest.GameMode == "Ranked"
        && createRequest.Day == 8
        && createRequest.Hour == 3
        && createRequest.PlayerRank == "Diamond"
        && createRequest.PlayerRating == 2410
        && createRequest.BuildChannel == "Ptr",
    "The create-request mapper should preserve basics, rank, run id, and build channel."
);

var stats = new PlayerStatsSnapshot
{
    MaxHealth = 125,
    Prestige = 11,
    Level = 9,
    Income = 7,
    Gold = 23,
};
var completion = Invoke<RunLogCompletion>(
    mapperType,
    null,
    "BuildRunLogCompletion",
    ["run_state_exit", Enum.Parse(runExitKindType, "Interrupted"), basics, stats, rank]
);
Assert(
    completion.Status == "abandoned"
        && completion.FinalDay == 8
        && completion.FinalHour == 3
        && completion.MaxHealth == 125
        && completion.Prestige == 11
        && completion.Level == 9
        && completion.Income == 7
        && completion.Gold == 23
        && completion.Victories == 10
        && completion.Losses == 2
        && completion.FinalPlayerRank == "Diamond"
        && completion.FinalPlayerRating == 2410
        && completion.Reason == "run_state_exit",
    "The completion mapper should preserve exit status and every supplied snapshot field."
);

var abandonment = Invoke<RunLogAbandonment>(
    mapperType,
    null,
    "BuildRunLogAbandonment",
    ["session_mismatch", basics]
);
Assert(
    abandonment.Status == "abandoned"
        && abandonment.FinalDay == 8
        && abandonment.FinalHour == 3
        && abandonment.Reason == "session_mismatch",
    "The abandonment mapper should preserve the final run basics and reason."
);

var nullBasicsArgs = new object?[] { null, rank, "server-run-42", "Ptr", null };
Assert(
    !Invoke<bool>(mapperType, null, "TryCreateRunLogCreateRequest", nullBasicsArgs)
        && nullBasicsArgs[4] == null,
    "Null basics should not produce a create request."
);

var herolessBasics = Activator.CreateInstance(basicsType)!;
SetProperty(basicsType, herolessBasics, "Day", 8);
var herolessArgs = new object?[] { herolessBasics, rank, "server-run-42", "Ptr", null };
Assert(
    !Invoke<bool>(mapperType, null, "TryCreateRunLogCreateRequest", herolessArgs)
        && herolessArgs[4] == null,
    "Basics without a hero should not produce a create request (the old Player-null guard)."
);

var blankRunIdArgs = new object?[] { basics, rank, "   ", "Ptr", null };
Assert(
    !Invoke<bool>(mapperType, null, "TryCreateRunLogCreateRequest", blankRunIdArgs)
        && blankRunIdArgs[4] == null,
    "A blank server run id should not produce a create request."
);

var completedRun = Invoke<RunLogCompletion>(
    mapperType,
    null,
    "BuildRunLogCompletion",
    ["run_state_exit", Enum.Parse(runExitKindType, "Completed"), basics, null, rank]
);
Assert(
    completedRun.Status == "completed"
        && completedRun.MaxHealth == null
        && completedRun.Prestige == null
        && completedRun.Level == null
        && completedRun.Income == null
        && completedRun.Gold == null
        && completedRun.FinalDay == 8
        && completedRun.Victories == 10,
    "A non-interrupted exit should map to completed, and missing stats should map to null fields while basics survive."
);

Console.WriteLine("RunLogging record mapping checks passed.");

static Type RequireType(string fullName)
{
    var assembly = Assembly.Load("BazaarPlusPlus");
    return assembly.GetType(fullName, throwOnError: false)
        ?? assembly
            .GetTypes()
            .FirstOrDefault(type =>
                type.FullName == fullName || type.Name == fullName.Split('.').Last()
            )
        ?? throw new InvalidOperationException($"Type not found: {fullName}");
}

static void SetProperty(Type type, object instance, string name, object? value)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    property.SetValue(instance, value);
}

static T Invoke<T>(Type type, object? instance, string name, object?[] args)
{
    var flags = BindingFlags.Public | BindingFlags.InvokeMethod;
    flags |= instance == null ? BindingFlags.Static : BindingFlags.Instance;
    var method = type.GetMethod(name, flags);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");

    return (T)method.Invoke(instance, args)!;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
