#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop.MonsterBoardPreview;

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
    // List precedence: account → rows → read → running sync → filters → failed sync → empty. A
    // failed sync sits after filters so a filter-emptied page still points at its filters.
    public static HistoryArchiveStatus ArchiveStatus(HistoryArchiveFacts facts)
    {
        var list = ListMessage(facts);
        var preview = PreviewOverride(facts, list);
        return new HistoryArchiveStatus(
            list,
            preview ?? BoardMessage(facts.PlayerBoard),
            preview ?? BoardMessage(facts.OpponentBoard),
            facts.Section == HistorySectionMode.Ghost && !facts.AccountKnown
                    ? HistoryPageLabelMode.Hidden
                : facts.PageLoading ? HistoryPageLabelMode.Loading
                : HistoryPageLabelMode.Totals
        );
    }

    private static string ListMessage(HistoryArchiveFacts facts)
    {
        if (facts.Section != HistorySectionMode.Ghost)
            return string.Empty;
        if (!facts.AccountKnown)
            return HistoryPanelText.GhostAccountUnavailable();
        if (facts.RowCount > 0)
            return string.Empty;
        if (facts.PageLoading)
            return HistoryPanelText.LoadingPreview();
        if (facts.PageLoadFailed)
            return HistoryPanelText.GhostHistoryReadFailed();
        if (facts.GhostSync == GhostSyncPhase.Running)
            return HistoryPanelText.SyncingGhostBattles();
        if (facts.FiltersActive)
            return HistoryPanelText.NoGhostFilterMatches();
        return facts.GhostSync == GhostSyncPhase.Failed
            ? HistoryPanelText.GhostSyncIncomplete()
            : HistoryPanelText.NoSavedGhostBattles();
    }

    // Null leaves each native board's own ready/empty/partial/failure message intact.
    private static string? PreviewOverride(HistoryArchiveFacts facts, string list)
    {
        if (facts.Section == HistorySectionMode.Ghost)
        {
            if (!facts.AccountKnown || facts.RowCount == 0)
                return list;
        }
        if (facts.Detail == HistoryDetailPhase.Loading)
            return HistoryPanelText.LoadingPreview();
        if (
            facts.Section != HistorySectionMode.Ghost
            || facts.SelectedGhostReplay is not { } replay
        )
            return facts.DetailFailed ? HistoryPanelText.PreviewRendererInitFailed() : null;
        if (replay.ActionInProgress)
            return replay.Availability == ReplayAvailability.Saved
                ? HistoryPanelText.StartingReplay()
                : HistoryPanelText.DownloadingGhostReplay();
        return replay.Availability switch
        {
            ReplayAvailability.Expired => HistoryPanelText.GhostReplayExpired(),
            ReplayAvailability.Unavailable => HistoryPanelText.GhostReplayPayloadUnavailable(),
            ReplayAvailability.Remote => replay.FailureMessage
                ?? HistoryPanelText.GhostReplayDownloadRequired(),
            _ => facts.DetailFailed || facts.Detail == HistoryDetailPhase.Missing
                ? replay.FailureMessage ?? HistoryPanelText.GhostLocalReplayUnreadable()
                : null,
        };
    }

    private static string BoardMessage(HistoryBoardFacts board) =>
        board.Status switch
        {
            NativeMonsterBoardStatus.Loading => HistoryPanelText.LoadingPreview(),
            NativeMonsterBoardStatus.Empty => HistoryPanelText.NoLocallyRenderableCards(),
            NativeMonsterBoardStatus.Failed => HistoryPanelText.PreviewRendererInitFailed(),
            NativeMonsterBoardStatus.Partial => HistoryPanelText.PartialPreview(board.PartialCount),
            _ => string.Empty,
        };

    public static string GhostDownloadFailureMessage(
        ReplayAvailability? availability,
        string? error,
        HistoryPanelReplayReasonCode reason
    )
    {
        if (availability == ReplayAvailability.Expired)
            return HistoryPanelText.GhostReplayExpired();
        if (
            availability == ReplayAvailability.Unavailable
            || reason
                is HistoryPanelReplayReasonCode.GhostArtifactInvalid
                    or HistoryPanelReplayReasonCode.GhostBattleMismatch
        )
            return HistoryPanelText.GhostReplayPayloadUnavailable();
        return HistoryPanelText.FailedToDownloadGhostReplay(error ?? HistoryPanelText.Unknown());
    }

    public static string GhostReplayUnavailableReason(HistoryBattleRecord battle) =>
        battle.Replay == ReplayAvailability.Expired
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

    // Linking requires uploads and a non-PTR build; PTR wins over disabled uploads, and the
    // guidance card is always visible. Unknown is treated like Online by policy (see
    // IGameBuildInfo) so a channel detection failure can never disable linking on production.
    internal static AccountLinkGate ResolveAccountLinkGate(
        bool uploadsEnabled,
        GameBuildChannel channel
    ) =>
        channel == GameBuildChannel.Ptr ? AccountLinkGate.PtrUnavailable
        : uploadsEnabled ? AccountLinkGate.Available
        : AccountLinkGate.UploadsDisabled;

    internal static HistoryAccountLinkCard ResolveAccountLinkCard(
        AccountLinkGate gate,
        bool hasAccount,
        bool linked,
        bool expanded,
        bool inProgress
    )
    {
        var rowActionText = linked
            ? HistoryPanelText.AccountLink.Relink()
            : HistoryPanelText.AccountLink.RowBind();
        var linkButtonText = inProgress
            ? HistoryPanelText.AccountLink.Linking()
            : HistoryPanelText.AccountLink.Button();
        var statusText = gate switch
        {
            AccountLinkGate.PtrUnavailable => HistoryPanelText.AccountLink.PtrUnavailable(),
            AccountLinkGate.UploadsDisabled => HistoryPanelText.AccountLink.UploadsDisabled(),
            _ when !hasAccount => HistoryPanelText.AccountLink.SignedOut(),
            _ when linked => HistoryPanelText.AccountLink.Linked(),
            _ => HistoryPanelText.AccountLink.NotLinked(),
        };
        var actionVisible = gate == AccountLinkGate.Available && hasAccount;
        var formVisible = actionVisible && expanded;
        var canSubmit = actionVisible && !inProgress;
        return new(
            statusText,
            actionVisible && linked ? HistoryPanelText.AccountLink.BindingPersists() : "",
            actionVisible,
            formVisible,
            rowActionText,
            linkButtonText,
            formVisible && !linked && !inProgress,
            canSubmit,
            canSubmit
        );
    }
}

// Whether this client may link a BazaarDB account; resolved from settings and build channel.
internal enum AccountLinkGate
{
    Available,
    UploadsDisabled,
    PtrUnavailable,
}

internal readonly record struct HistoryAccountLinkCard(
    string StatusText,
    string PersistenceText,
    bool ActionVisible,
    bool FormVisible,
    string RowActionText,
    string LinkButtonText,
    bool AlreadyLinkedButtonVisible,
    bool LinkButtonEnabled,
    bool InputEnabled
);
