#nullable enable
using System;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.Online;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelFactory
{
    public static HistoryPanelDependencies Create(
        IHistoryPanelRuntime runtime,
        ModOnlineClient onlineClient
    )
    {
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));
        if (onlineClient == null)
            throw new ArgumentNullException(nameof(onlineClient));

        HistoryPanelRepository? repository = null;
        if (!string.IsNullOrWhiteSpace(runtime.RunLogDatabasePath))
            repository = new HistoryPanelRepository(runtime.RunLogDatabasePath);

        var ghostSyncService = CreateGhostSyncService(repository, onlineClient);
        var dataService = new HistoryPanelDataService(repository, ghostSyncService);
        var replayService = new HistoryPanelReplayService(
            runtime.CombatReplayRuntimeAccessor,
            () => runtime.CombatReplayDirectoryPath,
            ghostSyncService
        );
        return new HistoryPanelDependencies(runtime, dataService, replayService, ghostSyncService);
    }

    private static GhostBattleSyncService? CreateGhostSyncService(
        HistoryPanelRepository? repository,
        ModOnlineClient onlineClient
    )
    {
        if (repository == null)
            return null;

        return new GhostBattleSyncService(repository, onlineClient);
    }
}
