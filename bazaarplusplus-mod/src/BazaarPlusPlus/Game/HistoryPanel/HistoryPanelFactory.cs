#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelFactory
{
    public static HistoryPanelDependencies Create(
        IHistoryPanelRunState runState,
        ModApiSession? modApiSession,
        Func<CombatReplayRuntime?> combatReplayRuntimeAccessor,
        string runLogDatabasePath,
        string combatReplayDirectoryPath,
        string combatReplayVideoDirectoryPath,
        string pluginsDirectoryPath,
        BazaarDbLinkClient? accountLinkClient = null,
        Func<bool>? isBazaarDbAccountLinkAvailable = null
    )
    {
        if (runState == null)
            throw new ArgumentNullException(nameof(runState));
        if (combatReplayRuntimeAccessor == null)
            throw new ArgumentNullException(nameof(combatReplayRuntimeAccessor));

        var databasePath = runLogDatabasePath ?? string.Empty;
        var replayDirectoryPath = combatReplayDirectoryPath ?? string.Empty;
        var videoDirectoryPath = combatReplayVideoDirectoryPath ?? string.Empty;
        var pluginsPath = pluginsDirectoryPath ?? string.Empty;

        // Null-degrade chain: empty/missing db path → no repository → no ghost sync →
        // data + replay services still construct, just without ghost capabilities.
        HistoryPanelRepository? repository = null;
        if (!string.IsNullOrWhiteSpace(databasePath))
            repository = new HistoryPanelRepository(databasePath);

        var ghostSyncService = CreateGhostSyncService(repository, modApiSession);
        var dataService = new HistoryPanelDataService(
            repository,
            ghostSyncService,
            () => replayDirectoryPath
        );
        var replayService = new HistoryPanelReplayService(
            combatReplayRuntimeAccessor,
            replayDirectoryPath,
            pluginsPath,
            videoDirectoryPath,
            ghostSyncService
        );
        var serverHealthProbe =
            modApiSession == null ? null : new HistoryPanelServerHealthProbe(modApiSession);
        return new HistoryPanelDependencies(
            runState,
            dataService,
            replayService,
            serverHealthProbe,
            accountLinkClient,
            isBazaarDbAccountLinkAvailable,
            replayDirectoryPath
        );
    }

    private static GhostBattleSyncService? CreateGhostSyncService(
        HistoryPanelRepository? repository,
        ModApiSession? modApiSession
    )
    {
        if (repository == null || modApiSession == null)
            return null;

        return new GhostBattleSyncService(repository, modApiSession);
    }
}
