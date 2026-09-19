#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.Supporters;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private Ui.HistoryPanelView? _uiView;
    private Rect _previewContainerBounds;
    private bool _hasPreviewContainerBounds;

    private void EnsureUi()
    {
        if (_uiView == null)
        {
            _uiView = new Ui.HistoryPanelView(
                transform,
                () => SetHistoryVisible(false),
                () => TryReplaySelectedBattle(false),
                () => TryReplaySelectedBattle(true),
                TryDeleteSelectedRun,
                TryCheckServerHealth,
                SubmitAccountLinkCode,
                ToggleAccountLinkForm,
                MarkAccountLinkedManually,
                SelectRun,
                SelectBattle,
                SetSectionMode,
                SetGhostBattleFilter,
                SetRunHero,
                ToggleGhostDayMin10
            );

            _uiView.SetPaginationActions(
                direction => _coordinator?.PageArchive(direction),
                direction => _coordinator?.PageBattles(direction)
            );
            _uiView.PreviewContainerBoundsChanged += OnPreviewContainerBoundsChanged;
            _uiView.OpponentBoundsChanged += bounds =>
            {
                _opponentBounds = bounds;
                if (_nativeOpponentBoard != null)
                    _nativeOpponentBoard.SetBounds(bounds);
                else
                    RefreshNativeHistoryBoards();
            };

            // Warm the native recorder on the UI thread before per-refresh gating.
            _coordinator?.PrewarmRecordingAvailability();
        }
        _uiView.EnsureCreated();
    }

    private void OnPreviewContainerBoundsChanged(Rect bounds)
    {
        _previewContainerBounds = bounds;
        _hasPreviewContainerBounds = true;
        if (_nativePlayerBoard != null)
            _nativePlayerBoard.SetBounds(bounds);
        else
            RefreshNativeHistoryBoards();
    }

    private void DisposeUi()
    {
        _uiView?.Dispose();
        _uiView = null;
    }

    private void SetUiVisible(bool visible)
    {
        _uiView?.SetVisible(visible);
    }

    private bool IsTextInputFocused()
    {
        return _uiView?.IsTextInputFocused() == true;
    }

    private void RefreshUi()
    {
        _uiView?.Refresh(BuildUiModel());
    }

    private void SetPreviewStatus(string? message, bool visible)
    {
        _uiView?.SetPreviewStatus(message, visible);
    }

    private HistoryPanelViewModel BuildUiModel()
    {
        var canReplaySelectedBattle = CanReplaySelectedBattle(out var replayUnavailableReason);
        var canRecordSelectedBattle = CanRecordSelectedBattle(out _);
        var canDeleteSelectedRun = CanDeleteSelectedRun(out _);
        var filteredRuns = FilteredRuns;
        var visibleRuns = filteredRuns.ToList();
        var visibleBattles =
            _state.SectionMode == HistorySectionMode.Ghost
                ? FilteredGhostBattles.ToList()
                : _state.Battles.ToList();

        var selectedBattle = ActiveSelectedBattle;
        var selectedRun = SelectedRun;
        var now = Time.unscaledTime;
        var databaseChip =
            _coordinator?.ResolveDatabaseChip()
            ?? HistoryPanelDecisions.ResolveDatabaseChip(false, false);
        var buttons = HistoryPanelButtonModel.Build(
            _state.ReplayActionInProgress,
            canReplaySelectedBattle,
            replayUnavailableReason,
            _coordinator?.GetReplayActionLabel(selectedBattle) ?? HistoryPanelText.Replay(),
            _runState?.IsInGameRun == true,
            canRecordSelectedBattle,
            _state.SectionMode == HistorySectionMode.Runs
                && selectedRun != null
                && _coordinator?.IsDeleteRunConfirmationActive(selectedRun.RunId, now) == true,
            canDeleteSelectedRun
        );

        var serverHealthDisplay = _state.ServerHealthProbeInProgress
            ? HistoryPanelServerHealthFormatter.Checking()
            : HistoryPanelServerHealthFormatter.Idle();
        var isBazaarDbLinked = _state.LocalLinkedHint;
        var hasAccount = !string.IsNullOrWhiteSpace(_state.CachedAccountId);
        var accountFormVisible = hasAccount && _state.AccountLinkExpanded;

        var statusSeverity = _state.StatusSeverity;

        return new HistoryPanelViewModel
        {
            PageLoading = _state.PageLoading,
            HasNewer =
                _state.SectionMode == HistorySectionMode.Ghost
                    ? _state.GhostPage.HasNewer
                    : _state.RunPage.HasNewer,
            HasOlder =
                _state.SectionMode == HistorySectionMode.Ghost
                    ? _state.GhostPage.HasOlder
                    : _state.RunPage.HasOlder,
            BattleHasNewer = _state.BattlePage.HasNewer,
            BattleHasOlder = _state.BattlePage.HasOlder,
            PageRange = HistoryPanelFormatter.PageRange(
                _state.SectionMode == HistorySectionMode.Ghost
                    ? _state.GhostPage.First
                    : _state.RunPage.First,
                _state.SectionMode == HistorySectionMode.Ghost
                    ? _state.GhostPage.Last
                    : _state.RunPage.Last
            ),
            AccountId = _state.CachedAccountId,
            Title = HistoryPanelText.Title(),
            Supporters = _supporters,
            DatabaseChipText = databaseChip.Text,
            DatabaseChipSeverity = databaseChip.Severity,
            ServerHealthButtonText = serverHealthDisplay.ButtonText,
            ServerHealthButtonEnabled = serverHealthDisplay.ButtonEnabled,
            AccountCardVisible = _dependencies?.IsBazaarDbAccountLinkAvailable?.Invoke() ?? false,
            AccountTitleText = HistoryPanelText.AccountLink.Title(),
            AccountWhyText = HistoryPanelText.AccountLink.Why(),
            AccountHintText = HistoryPanelText.AccountLink.Hint(),
            AccountRowStatusText =
                !hasAccount ? HistoryPanelText.AccountLink.SignedOut()
                : isBazaarDbLinked ? HistoryPanelText.AccountLink.Linked()
                : HistoryPanelText.AccountLink.NotLinked(),
            AccountRowActionText = isBazaarDbLinked
                ? HistoryPanelText.AccountLink.Relink()
                : HistoryPanelText.AccountLink.RowBind(),
            AccountRowActionVisible = hasAccount,
            AccountLinkCollapseText = HistoryPanelText.AccountLink.Collapse(),
            AccountLinkButtonText = _state.AccountLinkInProgress
                ? HistoryPanelText.AccountLink.Linking()
                : HistoryPanelText.AccountLink.Button(),
            AccountAlreadyLinkedButtonText =
                HistoryPanelText.AccountLink.AlreadyLinkedElsewhereButton(),
            AccountAlreadyLinkedButtonVisible =
                accountFormVisible && !isBazaarDbLinked && !_state.AccountLinkInProgress,
            AccountLinkButtonEnabled = !_state.AccountLinkInProgress && hasAccount,
            AccountLinkInputEnabled = !_state.AccountLinkInProgress && hasAccount,
            AccountLinkBannerText = _state.AccountLinkBannerMessage,
            AccountLinkBannerSeverity = _state.AccountLinkBannerSeverity,
            AccountLinkFormVisible = accountFormVisible,
            SectionMode = _state.SectionMode,
            GhostBattleFilter = _state.GhostBattleFilter,
            SelectedRunHero = _state.SelectedRunHero,
            GhostDayMin10 = _state.GhostDayMin10,
            StatusMessage = _state.StatusMessage,
            StatusSeverity = statusSeverity,
            Runs = visibleRuns,
            VisibleBattles = visibleBattles,
            SelectedRunIndex = _state.SelectedRunIndex,
            SelectedBattleIndex =
                _state.SectionMode == HistorySectionMode.Ghost
                    ? _state.SelectedGhostBattleIndex
                    : _state.SelectedBattleIndex,
            ReplayButtonText = buttons.ReplayButtonText,
            ReplayButtonEnabled = buttons.ReplayButtonEnabled,
            RecordAndReplayButtonText = buttons.RecordAndReplayButtonText,
            RecordAndReplayButtonEnabled = buttons.RecordAndReplayButtonEnabled,
            DeleteButtonText = buttons.DeleteButtonText,
            DeleteButtonEnabled = buttons.DeleteButtonEnabled,
        };
    }
}

internal sealed class HistoryPanelViewModel
{
    public string? AccountId { get; set; }

    public bool PageLoading { get; set; }
    public bool HasNewer { get; set; }
    public bool HasOlder { get; set; }
    public bool BattleHasNewer { get; set; }
    public bool BattleHasOlder { get; set; }
    public string PageRange { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    public IReadOnlyList<BPPSupporterSample> Supporters { get; set; } =
        new List<BPPSupporterSample>();

    public string DatabaseChipText { get; set; } = string.Empty;

    public string ServerHealthButtonText { get; set; } = string.Empty;

    public bool ServerHealthButtonEnabled { get; set; }

    public bool AccountCardVisible { get; set; }

    public string AccountTitleText { get; set; } = string.Empty;

    public string AccountWhyText { get; set; } = string.Empty;

    public string AccountHintText { get; set; } = string.Empty;

    public string AccountRowStatusText { get; set; } = string.Empty;

    public string AccountRowActionText { get; set; } = string.Empty;

    public bool AccountRowActionVisible { get; set; }

    public string AccountLinkCollapseText { get; set; } = string.Empty;

    public string AccountLinkButtonText { get; set; } = string.Empty;

    public string AccountAlreadyLinkedButtonText { get; set; } = string.Empty;

    public bool AccountAlreadyLinkedButtonVisible { get; set; }

    public bool AccountLinkButtonEnabled { get; set; }

    public bool AccountLinkInputEnabled { get; set; }

    public string? AccountLinkBannerText { get; set; }

    public StatusSeverity AccountLinkBannerSeverity { get; set; }

    public bool AccountLinkFormVisible { get; set; }

    public HistorySectionMode SectionMode { get; set; }

    public GhostBattleFilter GhostBattleFilter { get; set; }

    public string? SelectedRunHero { get; set; }

    public bool GhostDayMin10 { get; set; }

    public string? StatusMessage { get; set; }

    public StatusSeverity StatusSeverity { get; set; }

    public StatusSeverity DatabaseChipSeverity { get; set; }

    public List<HistoryRunRecord> Runs { get; set; } = new();

    public List<HistoryBattleRecord> VisibleBattles { get; set; } = new();

    public int SelectedRunIndex { get; set; }

    public int SelectedBattleIndex { get; set; }

    public string ReplayButtonText { get; set; } = string.Empty;

    public bool ReplayButtonEnabled { get; set; }

    public string RecordAndReplayButtonText { get; set; } = string.Empty;

    public bool RecordAndReplayButtonEnabled { get; set; }

    public string DeleteButtonText { get; set; } = string.Empty;

    public bool DeleteButtonEnabled { get; set; }
}
