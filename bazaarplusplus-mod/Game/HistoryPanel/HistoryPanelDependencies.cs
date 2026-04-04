#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Ghost;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelDependencies
{
    public HistoryPanelDependencies(
        IHistoryPanelRuntime runtime,
        HistoryPanelDataService dataService,
        HistoryPanelReplayService replayService,
        GhostBattleSyncService? ghostSyncService
    )
    {
        Runtime = runtime;
        DataService = dataService;
        ReplayService = replayService;
        GhostSyncService = ghostSyncService;
    }

    public IHistoryPanelRuntime Runtime { get; }

    public HistoryPanelDataService DataService { get; }

    public HistoryPanelReplayService ReplayService { get; }

    public GhostBattleSyncService? GhostSyncService { get; }
}
