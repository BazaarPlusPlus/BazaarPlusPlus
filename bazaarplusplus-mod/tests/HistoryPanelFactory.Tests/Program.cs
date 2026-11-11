#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Storage;

// HistoryPanelFactory.Create's signature closes over Func<CombatReplayRuntime?>, and resolving
// that MonoBehaviour type loads UnityEngine.CoreModule — unavailable in this exe-runner host.
// These tests pin the empty-db degrade semantics that Factory implements and the mount plan.

TestEmptyDbPathDegradesRepositoryAndGhostSync();
TestWhitespaceDbPathDegradesRepositoryAndGhostSync();
TestMountPlanRequiresReplayRuntimeAndOverlayHost();

Console.WriteLine("HistoryPanelFactory checks passed.");

void TestEmptyDbPathDegradesRepositoryAndGhostSync()
{
    AssertDegradedChain(BuildDataServiceForDbPath(string.Empty), "empty");
}

void TestWhitespaceDbPathDegradesRepositoryAndGhostSync()
{
    AssertDegradedChain(BuildDataServiceForDbPath("   "), "whitespace");
}

void TestMountPlanRequiresReplayRuntimeAndOverlayHost()
{
    Assert(
        HistoryPanelMountPlan.ShouldMount(true, true),
        "Replay runtime and overlay host should mount HistoryPanel."
    );
    Assert(
        !HistoryPanelMountPlan.ShouldMount(false, true)
            && !HistoryPanelMountPlan.ShouldMount(true, false),
        "Replay runtime and overlay host remain required mount dependencies."
    );
}

// Mirror HistoryPanelFactory's empty-path degrade chain without invoking Create (Unity-typed
// signature).
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
