#nullable enable
using System.Reflection;
using BepInEx.Logging;

var assembly = Assembly.Load("BazaarPlusPlus");
var runLifecycleType = RequireType("BazaarPlusPlus.Game.RunLifecycle.RunLifecycleModule");
var eventBusType = RequireType("BazaarPlusPlus.Core.Events.InMemoryBppEventBus");
var gameStateProbeType = RequireType("BazaarPlusPlus.GameInterop.GameStateProbe");
var runContextType = RequireType("BazaarPlusPlus.GameInterop.RunContextStore");
var runInitializedObservedType = RequireType("BazaarPlusPlus.Core.Events.RunInitializedObserved");
using var logSource = new ManualLogSource("run-lifecycle-tests");
var capturedLogs = new List<LogEventArgs>();
logSource.LogEvent += (_, args) => capturedLogs.Add(args);
InstallBppLog(logSource);

var eventBus =
    Activator.CreateInstance(eventBusType)
    ?? throw new InvalidOperationException("Failed to construct InMemoryBppEventBus.");
var gameStateProbe =
    Activator.CreateInstance(gameStateProbeType)
    ?? throw new InvalidOperationException("Failed to construct GameStateProbe.");
var runContext =
    Activator.CreateInstance(runContextType)
    ?? throw new InvalidOperationException("Failed to construct RunContextStore.");
var module =
    Activator.CreateInstance(runLifecycleType, [eventBus, gameStateProbe, runContext])
    ?? throw new InvalidOperationException("Failed to construct RunLifecycleModule.");

var firstObservedRun = Activator.CreateInstance(runInitializedObservedType)!;
SetProperty(runInitializedObservedType, firstObservedRun, "RunId", "12345678-full-run-id");
InvokePrivate(runLifecycleType, module, "OnRunInitializedObserved", [firstObservedRun]);
InvokePrivate(runLifecycleType, module, "OnRunInitializedObserved", [firstObservedRun]);
var secondObservedRun = Activator.CreateInstance(runInitializedObservedType)!;
SetProperty(runInitializedObservedType, secondObservedRun, "RunId", "87654321-second-run-id");
InvokePrivate(runLifecycleType, module, "OnRunInitializedObserved", [secondObservedRun]);
InvokePrivate(runLifecycleType, module, "OnRunInitializedObserved", [firstObservedRun]);

var startedLogs = capturedLogs
    .Where(args => args.Data?.ToString()?.Contains("event=run_lifecycle.run.started") == true)
    .ToArray();
Assert(startedLogs.Length == 2, "Each distinct run id should emit one lifecycle Info event.");
Assert(
    startedLogs.All(args => args.Level == LogLevel.Info),
    "Run lifecycle start events should be Info."
);
Assert(
    startedLogs[0].Data?.ToString()?.Contains("run_id=12345678") == true,
    "Run lifecycle start should render the short run id."
);
Assert(
    startedLogs.All(args =>
        args.Data?.ToString()?.Contains("12345678-full-run-id") != true
        && args.Data?.ToString()?.Contains("87654321-second-run-id") != true
    ),
    "Run lifecycle logs should not expose full run ids."
);

SetProperty(runContextType, runContext, "IsInGameRun", true);
SetProperty(runContextType, runContext, "CurrentServerRunId", "stale-finished-run");

InvokeSetInGameRun(runLifecycleType, module, false, "test reconciliation");

Assert(
    !GetRequiredProperty<bool>(runContextType, runContext, "IsInGameRun"),
    "Leaving a run should update IsInGameRun."
);
Assert(
    GetProperty(runContextType, runContext, "CurrentServerRunId") == null,
    "Leaving a run should clear the cached server run id."
);

SetProperty(runContextType, runContext, "CurrentServerRunId", "fresh-run-id");
InvokeSetInGameRun(runLifecycleType, module, true, "test enter");

Assert(
    GetRequiredProperty<bool>(runContextType, runContext, "IsInGameRun"),
    "Entering a run should update IsInGameRun."
);
Assert(
    (string?)GetProperty(runContextType, runContext, "CurrentServerRunId") == "fresh-run-id",
    "Entering a run should preserve the current server run id."
);

Console.WriteLine("RunLifecycle state checks passed.");

static Type RequireType(string fullName)
{
    return Assembly.Load("BazaarPlusPlus").GetType(fullName, throwOnError: true)!;
}

static void InvokeSetInGameRun(Type type, object instance, bool inGameRun, string reason)
{
    var method = type.GetMethod("SetInGameRun", BindingFlags.NonPublic | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.SetInGameRun");

    try
    {
        method.Invoke(instance, [inGameRun, reason]);
    }
    catch (TargetInvocationException ex)
        when (ex.InnerException is TypeInitializationException or FileNotFoundException)
    {
        // This path logs Unity/TheBazaar state for diagnostics. The side effects we care about
        // happen before that host-only logging runs, so the test can still assert state changes.
    }
}

static void InvokePrivate(Type type, object instance, string name, object?[] args)
{
    var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
    if (method == null)
        throw new InvalidOperationException($"Method not found: {type.FullName}.{name}");
    method.Invoke(instance, args);
}

static void InstallBppLog(ManualLogSource source)
{
    var bppLogType = RequireType("BazaarPlusPlus.Infrastructure.BppLog");
    var install = bppLogType.GetMethod(
        "Install",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
    );
    if (install == null)
        throw new InvalidOperationException("BppLog.Install was not found.");
    install.Invoke(null, [source]);
}

static object? GetProperty(Type type, object instance, string name)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    return property.GetValue(instance);
}

static T GetRequiredProperty<T>(Type type, object instance, string name)
{
    var value = GetProperty(type, instance, name);
    if (value is T typed)
        return typed;

    throw new InvalidOperationException($"Property value missing: {type.FullName}.{name}");
}

static void SetProperty(Type type, object instance, string name, object? value)
{
    var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
    if (property == null)
        throw new InvalidOperationException($"Property not found: {type.FullName}.{name}");

    property.SetValue(instance, value);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
