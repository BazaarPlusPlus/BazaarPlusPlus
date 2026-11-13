#nullable enable
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi.Clients;
using Xunit;

public sealed class HistoryPanelFactoryTests
{
    // The two degrade checks compose the panel through the production HistoryPanelFactory.Create.
    // Its signature names CombatReplayRuntime, a MonoBehaviour, so the test copies
    // UnityEngine.CoreModule next to itself (the csproj); Create reads no Unity state.

    [Fact]
    public void TestEmptyDbPathDegradesRepositoryAndGhostSync()
    {
        AssertDegradedChain(BuildDataServiceForDbPath(string.Empty), "empty");
    }

    [Fact]
    public void TestWhitespaceDbPathDegradesRepositoryAndGhostSync()
    {
        AssertDegradedChain(BuildDataServiceForDbPath("   "), "whitespace");
    }

    [Fact]
    public void TestMountPlanRequiresReplayRuntimeAndOverlayHost()
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

    // The production composition with a Mod API session, so a missing ghost sync can only come
    // from the missing repository.
    private static HistoryPanelDataService BuildDataServiceForDbPath(string runLogDatabasePath)
    {
        using var session = ModApiSession.TryCreate(
            "https://api.invalid",
            "0.0.0-test",
            platformKey: null,
            "HistoryPanelFactory.Tests",
            TimeSpan.FromSeconds(1)
        )!;
        return HistoryPanelFactory
            .Create(
                new FakeRunContext(),
                session,
                () => null,
                runLogDatabasePath,
                combatReplayDirectoryPath: string.Empty,
                combatReplayVideoDirectoryPath: string.Empty
            )
            .DataService;
    }

    private static void AssertDegradedChain(HistoryPanelDataService dataService, string pathKind)
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            Xunit.Assert.Fail(message);
    }
}

internal sealed class FakeRunContext : IRunContext
{
    public bool IsInGameRun { get; set; }

    public string? CurrentServerRunId { get; set; }

    public RunExitKind LastRunExitKind { get; set; }
}
