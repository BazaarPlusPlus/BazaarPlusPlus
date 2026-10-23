#nullable enable
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.Supporters;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// Builds the <see cref="HistoryPanelViewModel"/> both hosts render from the panel's state and
/// coordinator, so the desktop and Android panels show the same lists, labels and buttons. The
/// host supplies only what it alone knows: the frame clock, the supporter samples it shows, and
/// each native board's status (a host without native boards passes the default).
/// </summary>
public static class HistoryPanelViewModelBuilder
{
    // The battle the replay, favorite and board actions act on in the current section.
    public static HistoryBattleRecord? ActiveSelectedBattle(
        HistoryPanelState state,
        HistoryPanelCoordinator? coordinator
    ) =>
        state.SectionMode switch
        {
            HistorySectionMode.Ghost => state.GetSelectedGhostBattle(
                coordinator?.GetFilteredGhostBattles() ?? Array.Empty<HistoryBattleRecord>()
            ),
            HistorySectionMode.Favorites => state.GetSelectedFavorite()?.Battle,
            _ => state.GetSelectedBattle(),
        };

    public static HistoryRunRecord? SelectedRun(
        HistoryPanelState state,
        HistoryPanelCoordinator? coordinator
    ) => state.GetSelectedRun(coordinator?.GetFilteredRuns() ?? Array.Empty<HistoryRunRecord>());

    // Runs shows the opponent's board above yours; a Local-origin favorite keeps that layout.
    // Ghost, and a Ghost-origin favorite, show only the challenger's board (ADR-0002).
    public static bool ShowsBothBoards(HistoryPanelState state) =>
        state.SectionMode switch
        {
            HistorySectionMode.Runs => true,
            HistorySectionMode.Favorites => state.GetSelectedFavorite()?.Battle.Source
                == HistoryBattleSource.Local,
            _ => false,
        };

    public static HistoryArchiveStatus ArchiveStatus(
        HistoryPanelState state,
        HistoryPanelCoordinator? coordinator,
        HistoryBoardFacts playerBoard,
        HistoryBoardFacts opponentBoard
    ) =>
        HistoryPanelDecisions.ArchiveStatus(
            HistoryArchiveFacts.Observe(
                state,
                ActiveSelectedBattle(state, coordinator),
                playerBoard,
                opponentBoard
            )
        );

    public static HistoryPanelViewModel Build(
        HistoryPanelState state,
        HistoryPanelCoordinator? coordinator,
        IRunContext? runContext,
        Func<AccountLinkGate>? accountLinkGate,
        float now,
        IReadOnlyList<BPPSupporterSample> supporters,
        HistoryBoardFacts playerBoard,
        HistoryBoardFacts opponentBoard
    )
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var selectedBattle = ActiveSelectedBattle(state, coordinator);
        var selectedRun = SelectedRun(state, coordinator);
        string replayUnavailableReason;
        bool canReplaySelectedBattle;
        bool canRecordSelectedBattle;
        bool canDeleteSelectedRun;
        if (coordinator == null)
        {
            replayUnavailableReason = HistoryPanelText.PanelUnavailable();
            canReplaySelectedBattle = canRecordSelectedBattle = canDeleteSelectedRun = false;
        }
        else
        {
            canReplaySelectedBattle = coordinator.CanReplaySelectedBattle(
                selectedBattle,
                out replayUnavailableReason
            );
            canRecordSelectedBattle = coordinator.CanRecordSelectedBattle(selectedBattle, out _);
            canDeleteSelectedRun = coordinator.CanDeleteSelectedRun(selectedRun, out _);
        }
        var visibleRuns = (
            coordinator?.GetFilteredRuns() ?? Array.Empty<HistoryRunRecord>()
        ).ToList();
        var section = state.SectionMode;
        var visibleBattles = section switch
        {
            HistorySectionMode.Ghost => (
                coordinator?.GetFilteredGhostBattles() ?? Array.Empty<HistoryBattleRecord>()
            ).ToList(),
            HistorySectionMode.Favorites => state.Favorites.Select(f => f.Battle).ToList(),
            _ => state.Battles.ToList(),
        };
        var selectedFavorite =
            section == HistorySectionMode.Favorites ? state.GetSelectedFavorite() : null;

        var databaseChip =
            coordinator?.ResolveDatabaseChip()
            ?? HistoryPanelDecisions.ResolveDatabaseChip(false, false);
        var buttons = HistoryPanelButtonModel.Build(
            state.ReplayActionInProgress,
            canReplaySelectedBattle,
            replayUnavailableReason,
            coordinator?.GetReplayActionLabel(selectedBattle) ?? HistoryPanelText.Replay(),
            runContext?.IsInGameRun == true,
            canRecordSelectedBattle,
            state.SectionMode == HistorySectionMode.Runs
                && selectedRun != null
                && coordinator?.IsDeleteRunConfirmationActive(selectedRun.RunId, now) == true,
            canDeleteSelectedRun
        );

        var serverHealthDisplay = state.ServerHealthProbeInProgress
            ? HistoryPanelServerHealthFormatter.Checking()
            : HistoryPanelServerHealthFormatter.Idle();
        var accountCard = HistoryPanelDecisions.ResolveAccountLinkCard(
            accountLinkGate?.Invoke() ?? AccountLinkGate.UploadsDisabled,
            !string.IsNullOrWhiteSpace(state.CachedAccountId),
            state.LocalLinkedHint,
            state.AccountLinkExpanded,
            state.AccountLinkInProgress
        );

        var archiveStatus = ArchiveStatus(state, coordinator, playerBoard, opponentBoard);
        var favoriteButton = FavoriteButton(state, coordinator, selectedBattle, selectedFavorite);
        var deleteButtonText = buttons.DeleteButtonText;
        var deleteButtonEnabled = buttons.DeleteButtonEnabled;
        if (section == HistorySectionMode.Favorites)
        {
            deleteButtonText =
                selectedFavorite != null
                && coordinator?.IsRemoveFavoriteConfirmationActive(selectedFavorite.BattleId, now)
                    == true
                    ? HistoryPanelText.RemoveFavoriteConfirm()
                    : HistoryPanelText.RemoveFavorite();
            deleteButtonEnabled = selectedFavorite != null && !state.FavoriteActionInProgress;
        }

        return new HistoryPanelViewModel
        {
            PageLoading = state.PageLoading,
            ArchiveEmptyMessage = archiveStatus.ListMessage,
            HasNewer = section switch
            {
                HistorySectionMode.Ghost => state.GhostPage.HasNewer,
                HistorySectionMode.Favorites => state.FavoritePage.HasNewer,
                _ => state.RunPage.HasNewer,
            },
            HasOlder = section switch
            {
                HistorySectionMode.Ghost => state.GhostPage.HasOlder,
                HistorySectionMode.Favorites => state.FavoritePage.HasOlder,
                _ => state.RunPage.HasOlder,
            },
            BattleHasNewer = state.BattlePage.HasNewer,
            BattleHasOlder = state.BattlePage.HasOlder,
            PageLabel = archiveStatus.PageLabel switch
            {
                HistoryPageLabelMode.Loading => HistoryPanelText.LoadingPreview(),
                HistoryPageLabelMode.Hidden => string.Empty,
                _ => section switch
                {
                    HistorySectionMode.Ghost => HistoryPanelFormatter.PageRange(
                        state.GhostPage,
                        ghost: true
                    ),
                    HistorySectionMode.Favorites => HistoryPanelFormatter.PageRange(
                        state.FavoritePage,
                        ghost: true
                    ),
                    _ => HistoryPanelFormatter.PageRange(state.RunPage),
                },
            },
            AccountId = state.CachedAccountId,
            Title = HistoryPanelText.Title(),
            Supporters = supporters ?? Array.Empty<BPPSupporterSample>(),
            DatabaseChipText = databaseChip.Text,
            DatabaseChipSeverity = databaseChip.Severity,
            ServerHealthButtonText = serverHealthDisplay.ButtonText,
            ServerHealthButtonEnabled = serverHealthDisplay.ButtonEnabled,
            AccountTitleText = HistoryPanelText.AccountLink.Title(),
            AccountWhyText = HistoryPanelText.AccountLink.Why(),
            AccountHintText = HistoryPanelText.AccountLink.Hint(),
            AccountRowStatusText = accountCard.StatusText,
            AccountPersistenceText = accountCard.PersistenceText,
            AccountRowActionText = accountCard.RowActionText,
            AccountRowActionVisible = accountCard.ActionVisible,
            AccountLinkCollapseText = HistoryPanelText.AccountLink.Collapse(),
            AccountLinkButtonText = accountCard.LinkButtonText,
            AccountAlreadyLinkedButtonText =
                HistoryPanelText.AccountLink.AlreadyLinkedElsewhereButton(),
            AccountAlreadyLinkedButtonVisible = accountCard.AlreadyLinkedButtonVisible,
            AccountLinkButtonEnabled = accountCard.LinkButtonEnabled,
            AccountLinkInputEnabled = accountCard.InputEnabled,
            AccountLinkBannerText = state.AccountLinkBannerMessage,
            AccountLinkBannerSeverity = state.AccountLinkBannerSeverity,
            AccountLinkFormVisible = accountCard.FormVisible,
            SectionMode = state.SectionMode,
            GhostBattleFilter = state.GhostBattleFilter,
            SelectedRunHero = state.SelectedRunHero,
            GhostDayMin10 = state.GhostDayMin10,
            StatusMessage = state.StatusMessage,
            StatusSeverity = state.StatusSeverity,
            Runs = visibleRuns,
            VisibleBattles = visibleBattles,
            SelectedRunIndex = state.SelectedRunIndex,
            SelectedBattleIndex = section switch
            {
                HistorySectionMode.Ghost => state.SelectedGhostBattleIndex,
                HistorySectionMode.Favorites => state.SelectedFavoriteIndex,
                _ => state.SelectedBattleIndex,
            },
            FavoriteRowLabels = state
                .Favorites.Select(HistoryPanelFormatter.FavoriteListText)
                .ToList(),
            ShowsBothBoards = ShowsBothBoards(state),
            FavoritesTabVisible = coordinator?.FavoritesAvailable == true,
            FavoriteButtonVisible = favoriteButton.Visible,
            FavoriteButtonText = favoriteButton.Text,
            FavoriteButtonEnabled = favoriteButton.Enabled,
            SelectedFavoriteBattleId = selectedFavorite?.BattleId,
            SelectedFavoriteNote = selectedFavorite?.Note ?? string.Empty,
            ReplayButtonText = buttons.ReplayButtonText,
            ReplayButtonEnabled = buttons.ReplayButtonEnabled,
            RecordAndReplayButtonText = buttons.RecordAndReplayButtonText,
            RecordAndReplayButtonEnabled = buttons.RecordAndReplayButtonEnabled,
            DeleteButtonVisible = section != HistorySectionMode.Ghost,
            DeleteButtonText = deleteButtonText,
            DeleteButtonEnabled = deleteButtonEnabled,
        };
    }

    // Runs and Ghost favorite the selected battle (or jump to it once it is one); Favorites edits
    // the selected favorite's note.
    private static (bool Visible, string Text, bool Enabled) FavoriteButton(
        HistoryPanelState state,
        HistoryPanelCoordinator? coordinator,
        HistoryBattleRecord? selectedBattle,
        HistoryFavoriteRecord? selectedFavorite
    )
    {
        if (coordinator?.FavoritesAvailable != true)
            return (false, string.Empty, false);
        if (state.SectionMode == HistorySectionMode.Favorites)
            return (
                true,
                HistoryPanelText.EditFavoriteNote(),
                selectedFavorite != null && !state.FavoriteActionInProgress
            );
        if (state.FavoriteActionInProgress)
            return (true, HistoryPanelText.SavingFavorite(), false);
        return (
            true,
            coordinator.IsSelectedBattleFavorite(selectedBattle)
                ? HistoryPanelText.FavoritedBattle()
                : HistoryPanelText.FavoriteBattle(),
            selectedBattle != null
        );
    }
}
