#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Storage;

// HistoryPanelFactory.Create's signature closes over Func<CombatReplayRuntime?>, and resolving
// that MonoBehaviour type loads UnityEngine.CoreModule — unavailable in this exe-runner host.
// These tests pin the empty-db degrade semantics that Factory implements and the mount plan.

TestEmptyDbPathDegradesRepositoryAndGhostSync();
TestWhitespaceDbPathDegradesRepositoryAndGhostSync();
TestMountPlanPreservesLocalHistoryWithoutSession();

Console.WriteLine("HistoryPanelFactory checks passed.");

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
