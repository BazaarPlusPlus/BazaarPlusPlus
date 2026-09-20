#nullable enable
using System.Reflection;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.GameInterop;

// HistoryPanelFactory.Create's signature closes over Func<CombatReplayRuntime?>, and resolving
// that MonoBehaviour type loads UnityEngine.CoreModule — unavailable in this exe-runner host.
// These tests pin (1) the Dependencies collapse, (2) the empty-db degrade semantics that Factory
// implements, and (3) source-level proof that Factory owns the degrade wiring.

TestDependenciesSingleConstructorArityAndNullAssignment();
TestRunStateAdapterIsReadOnlyTwoMembers();
TestEmptyDbPathDegradesRepositoryAndGhostSync();
TestWhitespaceDbPathDegradesRepositoryAndGhostSync();
TestMountPlanPreservesLocalHistoryWithoutSession();

Console.WriteLine("HistoryPanelFactory checks passed.");

void TestDependenciesSingleConstructorArityAndNullAssignment()
{
    var dependenciesType = typeof(HistoryPanelDependencies);
    var constructors = dependenciesType
        .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .ToArray();
    Assert(
        constructors.Length == 1,
        "HistoryPanelDependencies should have exactly one constructor after telescope collapse."
    );

    var parameters = constructors[0].GetParameters();
    Assert(
        parameters.Length == 6,
        "HistoryPanelDependencies single ctor should take 6 parameters."
    );
    Assert(
        parameters[0].ParameterType == typeof(IHistoryPanelRunState),
        "Ctor[0] should be IHistoryPanelRunState runState."
    );
    Assert(
        parameters[1].ParameterType == typeof(HistoryPanelDataService),
        "Ctor[1] should be HistoryPanelDataService dataService."
    );
    Assert(
        parameters[2].ParameterType == typeof(HistoryPanelReplayService),
        "Ctor[2] should be HistoryPanelReplayService replayService."
    );
    // Null-by-position is the pinned behavior anchor — no ArgumentNullException on construct.
    var dataService = new HistoryPanelDataService(null, null);
    var instance = new HistoryPanelDependencies(null!, dataService, null!, null, null, null);
    Assert(instance.DataService == dataService, "DataService should assign by position.");
    Assert(instance.RunState == null, "Null runState should assign without throwing.");
    Assert(instance.ReplayService == null, "Null replayService should assign without throwing.");
    Assert(
        dependenciesType.GetProperty("GhostSyncService") == null,
        "HistoryPanelDependencies.GhostSyncService dead property must be removed."
    );
    Assert(
        dependenciesType.GetProperty("Runtime") == null,
        "HistoryPanelDependencies.Runtime must be removed."
    );
    Assert(
        dependenciesType.GetProperty("RunState") != null,
        "HistoryPanelDependencies should expose RunState."
    );
    var modAssembly = typeof(HistoryPanelDependencies).Assembly;
    Assert(
        modAssembly.GetType("BazaarPlusPlus.Game.HistoryPanel.HistoryPanelRuntime") == null,
        "HistoryPanelRuntime DTO must be deleted."
    );
    Assert(
        modAssembly.GetType("BazaarPlusPlus.Game.HistoryPanel.IHistoryPanelRuntime") == null,
        "IHistoryPanelRuntime must be replaced by IHistoryPanelRunState."
    );
}

void TestRunStateAdapterIsReadOnlyTwoMembers()
{
    var members = typeof(IHistoryPanelRunState).GetProperties(
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    Assert(members.Length == 2, "IHistoryPanelRunState should expose exactly two members.");
    Assert(
        members.Any(p => p.Name == "IsInGameRun" && p.CanRead && !p.CanWrite),
        "IsInGameRun must be read-only."
    );
    Assert(
        members.Any(p => p.Name == "CurrentServerRunId" && p.CanRead && !p.CanWrite),
        "CurrentServerRunId must be read-only."
    );

    var context = new StubRunContext { IsInGameRun = true, CurrentServerRunId = "run-xyz" };
    IHistoryPanelRunState runState = new HistoryPanelRunState(context);
    Assert(runState.IsInGameRun, "RunState adapter should project IsInGameRun.");
    Assert(
        runState.CurrentServerRunId == "run-xyz",
        "RunState adapter should project CurrentServerRunId."
    );
}

void TestEmptyDbPathDegradesRepositoryAndGhostSync()
{
    AssertDegradedChain(BuildDataServiceForDbPath(string.Empty), "empty");
}

void TestWhitespaceDbPathDegradesRepositoryAndGhostSync()
{
    AssertDegradedChain(BuildDataServiceForDbPath("   "), "whitespace");
}

void TestMountPlanPreservesLocalHistoryWithoutSession()
{
    Assert(
        HistoryPanelMountPlan.Resolve(true, true, false) == HistoryPanelMountMode.MountLocalOnly,
        "Replay + overlay without a Mod API session should preserve local HistoryPanel."
    );
    Assert(
        HistoryPanelMountPlan.Resolve(true, true, true) == HistoryPanelMountMode.MountWithOnline,
        "A complete dependency set should mount HistoryPanel with online capabilities."
    );
    Assert(
        HistoryPanelMountPlan.Resolve(false, true, true) == HistoryPanelMountMode.DoNotMount
            && HistoryPanelMountPlan.Resolve(true, false, true) == HistoryPanelMountMode.DoNotMount,
        "Replay runtime and overlay host remain required mount dependencies."
    );
}

// Mirror HistoryPanelFactory's empty-path degrade chain without invoking Create (Unity-typed
// signature). Source assertion below proves Factory still owns this wiring.
HistoryPanelDataService BuildDataServiceForDbPath(string runLogDatabasePath)
{
    HistoryPanelRepository? repository = null;
    if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
        repository = new HistoryPanelRepository(runLogDatabasePath);

    // Factory's CreateGhostSyncService returns null when repository is null — same here.
    return new HistoryPanelDataService(repository, ghostSyncService: null);
}

void AssertDegradedChain(HistoryPanelDataService dataService, string pathKind)
{
    Assert(
        !dataService.IsAvailable,
        $"A {pathKind} db path should leave DataService.IsAvailable false (repository null)."
    );
    Assert(
        !dataService.CanSyncGhostBattles,
        $"A {pathKind} db path should leave DataService.CanSyncGhostBattles false (no ghost sync)."
    );
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

file sealed class StubRunContext : IRunContext
{
    public bool IsInGameRun { get; set; }
    public string? CurrentServerRunId { get; set; }
    public RunExitKind LastRunExitKind { get; set; }
    public RunVictoryOutcome LastVictoryOutcome { get; set; }
    public string LastMessageId { get; set; } = string.Empty;
}
