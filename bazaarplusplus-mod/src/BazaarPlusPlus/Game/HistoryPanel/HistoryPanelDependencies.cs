#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelDependencies
{
    public HistoryPanelDependencies(
        IHistoryPanelRuntime runtime,
        HistoryPanelDataService dataService,
        HistoryPanelReplayService replayService,
        GhostBattleSyncService? ghostSyncService
    )
        : this(runtime, dataService, replayService, ghostSyncService, null) { }

    public HistoryPanelDependencies(
        IHistoryPanelRuntime runtime,
        HistoryPanelDataService dataService,
        HistoryPanelReplayService replayService,
        GhostBattleSyncService? ghostSyncService,
        IHistoryPanelServerHealthProbe? serverHealthProbe
    )
    {
        Runtime = runtime;
        DataService = dataService;
        ReplayService = replayService;
        GhostSyncService = ghostSyncService;
        ServerHealthProbe = serverHealthProbe;
    }

    public IHistoryPanelRuntime Runtime { get; }

    public HistoryPanelDataService DataService { get; }

    public HistoryPanelReplayService ReplayService { get; }

    public GhostBattleSyncService? GhostSyncService { get; }

    public IHistoryPanelServerHealthProbe? ServerHealthProbe { get; }
}
