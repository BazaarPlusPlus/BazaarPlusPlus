#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal readonly struct HistoryPanelDatabaseChip
{
    public HistoryPanelDatabaseChip(string text, StatusSeverity severity)
    {
        Text = text;
        Severity = severity;
    }

    public string Text { get; }

    public StatusSeverity Severity { get; }
}

internal static class HistoryPanelDecisions
{
    public static string GhostArchiveEmptyMessage(HistoryPanelState state)
    {
        if (state.SectionMode != HistorySectionMode.Ghost)
            return string.Empty;
        if (string.IsNullOrWhiteSpace(state.CachedAccountId))
            return HistoryPanelText.GhostAccountUnavailable();
        if (state.GhostBattles.Count > 0)
            return string.Empty;
        if (state.PageLoading)
            return HistoryPanelText.LoadingPreview();
        if (state.PageLoadFailed)
            return HistoryPanelText.GhostHistoryReadFailed();
        if (state.GhostSyncInProgress)
            return HistoryPanelText.SyncingGhostBattles();
        return state.GhostBattleFilter != GhostBattleFilter.All || state.GhostDayMin10
            ? HistoryPanelText.NoGhostFilterMatches()
            : HistoryPanelText.NoSavedGhostBattles();
    }

    // Null leaves the native renderer's own ready/empty/partial/failure message intact.
    public static string? PreviewStatusOverride(
        HistoryPanelState state,
        HistoryBattleRecord? battle
    )
    {
        if (state.SectionMode == HistorySectionMode.Ghost)
        {
            if (string.IsNullOrWhiteSpace(state.CachedAccountId))
                return HistoryPanelText.GhostAccountUnavailable();
            if (battle == null)
                return GhostArchiveEmptyMessage(state);
        }
        if (state.DetailLoading)
            return HistoryPanelText.LoadingPreview();
        if (
            state.SectionMode != HistorySectionMode.Ghost
            || battle?.Source != HistoryBattleSource.Ghost
        )
            return state.DetailFailed ? HistoryPanelText.PreviewRendererInitFailed() : null;
        if (state.ReplayActionInProgress && state.ReplayActionBattleId == battle.BattleId)
            return battle.ReplayDownloaded
                ? HistoryPanelText.StartingReplay()
                : HistoryPanelText.DownloadingGhostReplay();
        if (!battle.ReplayDownloaded)
        {
            if (battle.GhostReplayState == "expired")
                return HistoryPanelText.GhostReplayExpired();
            if (battle.GhostReplayState == "unavailable_payload")
                return HistoryPanelText.GhostReplayPayloadUnavailable();
            if (state.ReplayActionBattleId == battle.BattleId && state.ReplayFailureMessage != null)
                return state.ReplayFailureMessage;
            return battle.ReplayAvailable
                ? HistoryPanelText.GhostReplayDownloadRequired()
                : HistoryPanelText.GhostReplayPayloadUnavailable();
        }
        if (
            state.DetailFailed
            || (state.DetailBattleId == battle.BattleId && state.DetailSnapshots == null)
        )
            return
                state.ReplayActionBattleId == battle.BattleId && state.ReplayFailureMessage != null
                ? state.ReplayFailureMessage
                : HistoryPanelText.GhostLocalReplayUnreadable();
        return null;
    }

    public static string GhostDownloadFailureMessage(
        string? error,
        HistoryPanelReplayReasonCode reason
    )
    {
        if (error == "ghost_replay_expired")
            return HistoryPanelText.GhostReplayExpired();
        if (
            error == "ghost_replay_unavailable_payload"
            || reason
                is HistoryPanelReplayReasonCode.GhostArtifactInvalid
                    or HistoryPanelReplayReasonCode.GhostBattleMismatch
        )
            return HistoryPanelText.GhostReplayPayloadUnavailable();
        return HistoryPanelText.FailedToDownloadGhostReplay(error ?? HistoryPanelText.Unknown());
    }

    public static string GhostReplayUnavailableReason(HistoryBattleRecord battle) =>
        battle.GhostReplayState == "expired"
            ? HistoryPanelText.GhostReplayExpired()
            : HistoryPanelText.GhostReplayPayloadUnavailable();

    public static bool CanDeleteRun(
        HistorySectionMode sectionMode,
        HistoryRunRecord? selectedRun,
        bool isInGameRun,
        string? currentServerRunId,
        bool isRepositoryAvailable,
        out string reason
    )
    {
        if (sectionMode == HistorySectionMode.Ghost)
        {
            reason = HistoryPanelText.GhostDeleteUnavailable();
            return false;
        }

        if (selectedRun == null)
        {
            reason = HistoryPanelText.SelectRunToDelete();
            return false;
        }

        if (string.Equals(selectedRun.RawStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            reason = HistoryPanelText.ActiveRunDeleteUnavailable();
            return false;
        }

        if (
            isInGameRun
            && string.Equals(currentServerRunId, selectedRun.RunId, StringComparison.Ordinal)
        )
        {
            reason = HistoryPanelText.CurrentGameplayRunDeleteUnavailable();
            return false;
        }

        if (!isRepositoryAvailable)
        {
            reason = HistoryPanelText.RunLogRepositoryUnavailable();
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static HistoryPanelDatabaseChip ResolveDatabaseChip(
        bool isRepositoryAvailable,
        bool databaseExists
    )
    {
        if (!isRepositoryAvailable)
            return new HistoryPanelDatabaseChip(
                HistoryPanelText.DatabaseChip(HistoryPanelText.DatabaseUnavailable()),
                StatusSeverity.Failure
            );

        return databaseExists
            ? new HistoryPanelDatabaseChip(
                HistoryPanelText.DatabaseChip(HistoryPanelText.DatabaseConnected()),
                StatusSeverity.Success
            )
            : new HistoryPanelDatabaseChip(
                HistoryPanelText.DatabaseChip(HistoryPanelText.DatabaseMissing()),
                StatusSeverity.Neutral
            );
    }

    // Account-link card gate: data sharing must be on AND the build must not be PTR.
    // Unknown is treated like Online by policy (see IGameBuildInfo) so a channel
    // detection failure can never hide the card on a production build.
    internal static bool IsAccountLinkCardAvailable(
        bool dataSharingEnabled,
        GameBuildChannel channel
    ) => dataSharingEnabled && channel != GameBuildChannel.Ptr;
}
