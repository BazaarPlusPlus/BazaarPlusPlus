#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelFactory
{
    public static HistoryPanelDependencies Create(
        IHistoryPanelRuntime runtime,
        ModOnlineClient onlineClient
    ) => Create(runtime, onlineClient, null);

    public static HistoryPanelDependencies Create(
        IHistoryPanelRuntime runtime,
        ModOnlineClient onlineClient,
        BazaarDbLinkClient? accountLinkClient,
        Func<bool>? isBazaarDbAccountLinkAvailable = null
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
        var dataService = new HistoryPanelDataService(
            repository,
            ghostSyncService,
            () => runtime.CombatReplayDirectoryPath
        );
        var replayService = new HistoryPanelReplayService(
            runtime.CombatReplayRuntimeAccessor,
            () => runtime.CombatReplayDirectoryPath,
            () => runtime.PluginsDirectoryPath,
            () => runtime.CombatReplayVideoDirectoryPath,
            ghostSyncService
        );
        var serverHealthProbe = new HistoryPanelServerHealthProbe(onlineClient);
        return new HistoryPanelDependencies(
            runtime,
            dataService,
            replayService,
            ghostSyncService,
            serverHealthProbe,
            accountLinkClient,
            isBazaarDbAccountLinkAvailable
        );
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
