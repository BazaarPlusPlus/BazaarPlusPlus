#nullable enable
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Favorites;
using BazaarPlusPlus.Game.HistoryPanel.Ghost;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.ModApi.Clients;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

// The desktop composition of History's Core coordinator: its ports are Unity's frame clock and
// PlayerPrefs, the game's client-cache profile, the game-typed PvP evidence, and the game's
// static card data.
internal static class HistoryPanelFactory
{
    public static HistoryPanelDependencies Create(
        IRunContext runContext,
        ModApiSession? modApiSession,
        Func<CombatReplayRuntime?> combatReplayRuntimeAccessor,
        string runLogDatabasePath,
        string combatReplayDirectoryPath,
        string combatReplayVideoDirectoryPath,
        BazaarDbLinkClient? accountLinkClient = null,
        Func<AccountLinkGate>? accountLinkGate = null
    )
    {
        if (runContext == null)
            throw new ArgumentNullException(nameof(runContext));
        if (combatReplayRuntimeAccessor == null)
            throw new ArgumentNullException(nameof(combatReplayRuntimeAccessor));

        var databasePath = runLogDatabasePath ?? string.Empty;
        var replayDirectoryPath = combatReplayDirectoryPath ?? string.Empty;
        var videoDirectoryPath = combatReplayVideoDirectoryPath ?? string.Empty;

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
            videoDirectoryPath,
            BppClientCacheBridge.TryGetProfileAccountId,
            ghostSyncService
        );
        // Lambdas, not method groups: Unity is resolved only when the clock or a hint is read,
        // so a test without Unity can still compose the panel.
        return new HistoryPanelDependencies(
            runContext,
            dataService,
            replayService,
            modApiSession,
            accountLinkClient,
            accountLinkGate,
            new BazaarDbAccountLinkStore(
                key => PlayerPrefs.HasKey(key),
                key =>
                {
                    PlayerPrefs.SetString(key, "1");
                    PlayerPrefs.Save();
                }
            ),
            () => Time.unscaledTime,
            BppClientCacheBridge.TryGetProfileAccountId
        )
        {
            Favorites =
                repository == null
                    ? null
                    : new HistoryFavoriteService(
                        repository,
                        replayDirectoryPath,
                        (
                            string battleId,
                            out PvpBattleManifest? manifest,
                            out PvpReplayPayload? payload
                        ) =>
                        {
                            manifest = null;
                            payload = null;
                            return combatReplayRuntimeAccessor()
                                    ?.TryLoadSavedBattle(battleId, out manifest, out payload)
                                ?? SavedBattleLoadOutcome.Unavailable;
                        },
                        replayService.LoadGhostReplayAsync,
                        (out string reason) =>
                        {
                            var runtime = combatReplayRuntimeAccessor();
                            if (runtime != null)
                                return runtime.CanReplaySavedCombats(out reason);
                            reason = HistoryPanelText.CombatReplayRuntimeUnavailable();
                            return false;
                        },
                        (manifest, payload, recordVideo, source) =>
                        {
                            var runtime = combatReplayRuntimeAccessor();
                            return runtime != null
                                && runtime.ReplayLoadedBattle(
                                    manifest,
                                    payload,
                                    recordVideo,
                                    source
                                );
                        }
                    ),
        };
    }

    private static GhostBattleSyncService? CreateGhostSyncService(
        HistoryPanelRepository? repository,
        ModApiSession? modApiSession
    )
    {
        if (repository == null || modApiSession == null)
            return null;

        return new GhostBattleSyncService(
            repository,
            modApiSession,
            BppClientCacheBridge.TryGetProfileAccountId,
            DesktopPvpBattleEvidence.Instance,
            HistoryBattlePreviewProjection.HasCardTemplate
        );
    }
}
