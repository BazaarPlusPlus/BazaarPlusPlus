#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.ModApi;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelFactory
{
    public static HistoryPanelDependencies Create(IHistoryPanelRuntime runtime)
    {
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));

        HistoryPanelRepository? repository = null;
        if (!string.IsNullOrWhiteSpace(runtime.RunLogDatabasePath))
            repository = new HistoryPanelRepository(runtime.RunLogDatabasePath);

        var ghostSyncService = CreateGhostSyncService(runtime, repository);
        var dataService = new HistoryPanelDataService(
            repository,
            ghostSyncService,
            TryGetCurrentPlayerAccountId
        );
        var replayService = new HistoryPanelReplayService(
            runtime.CombatReplayRuntimeAccessor,
            () => runtime.CombatReplayDirectoryPath,
            ghostSyncService
        );
        return new HistoryPanelDependencies(runtime, dataService, replayService, ghostSyncService);
    }

    private static GhostBattleSyncService? CreateGhostSyncService(
        IHistoryPanelRuntime runtime,
        HistoryPanelRepository? repository
    )
    {
        if (
            repository == null
            || BppRuntimeHost.Config.EnableCommunityContributionConfig?.Value != true
        )
            return null;

        var identityPath = BppRuntimeHost.Paths.RunUploadInstallIdentityPath;
        var clientStatePath = BppRuntimeHost.Paths.RunUploadClientStatePath;
        var privateKeyPath = BppRuntimeHost.Paths.RunUploadPrivateKeyPath;
        var context = ModApiBootstrapContext.TryCreate(
            runtime.RunLogDatabasePath,
            runtime.CombatReplayDirectoryPath,
            identityPath,
            clientStatePath,
            privateKeyPath,
            ModApiDefaults.ApiBaseUrl
        );
        if (context == null)
            return null;

        return new GhostBattleSyncService(
            repository,
            context.CreateIdentityStore(),
            context.CreateClientStateStore(),
            context.CreateKeyStore(),
            context.Routes,
            timeout: TimeSpan.FromSeconds(10)
        );
    }

    private static string? TryGetCurrentPlayerAccountId()
    {
        try
        {
            return BppClientCacheBridge.TryGetProfileAccountId();
        }
        catch
        {
            return null;
        }
    }
}
