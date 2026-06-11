#nullable enable
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Ui;
using BazaarPlusPlus.Game.Supporters;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private HistoryPanelUiToolkitView? _uiView;
    private Rect _previewContainerBounds;
    private bool _hasPreviewContainerBounds;
    private bool _previewContainerBoundsChanged;

    private void EnsureUi()
    {
        if (_uiView == null)
        {
            _uiView = new HistoryPanelUiToolkitView(
                transform,
                () => SetHistoryVisible(false),
                () => TryReplaySelectedBattle(false),
                () => TryReplaySelectedBattle(true),
                TryDeleteSelectedRun,
                TryCheckServerHealth,
                SelectRun,
                SelectBattle,
                SetSectionMode,
                SetGhostBattleFilter
            );
            _uiView.PreviewContainerBoundsChanged += OnPreviewContainerBoundsChanged;

            // First panel open: warm the FFmpeg locator cache off the UI thread so the record
            // button's per-refresh availability gate never incurs the ~2s liveness probe.
            _coordinator?.PrewarmRecordingAvailability();
        }
        _uiView.EnsureCreated();
    }

    private void OnPreviewContainerBoundsChanged(Rect bounds)
    {
        _previewContainerBoundsChanged =
            !_hasPreviewContainerBounds || !RectApproximately(_previewContainerBounds, bounds);
        _previewContainerBounds = bounds;
        _hasPreviewContainerBounds = true;

        if (_battleBoardPreview == null)
            return;

        if (ApplyPreviewContainerBounds(bounds) && IsVisible)
            RefreshSelectedBattlePreview();
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

    private void RefreshUi()
    {
        _uiView?.Refresh(BuildUiModel());
    }

    private void SetPreviewStatus(string? message, bool visible)
    {
        _uiView?.SetPreviewStatus(message, visible);
    }

    private static bool RectApproximately(Rect left, Rect right) =>
        Mathf.Approximately(left.x, right.x)
        && Mathf.Approximately(left.y, right.y)
        && Mathf.Approximately(left.width, right.width)
        && Mathf.Approximately(left.height, right.height);

    private HistoryPanelUiToolkitModel BuildUiModel()
    {
        var canReplaySelectedBattle = CanReplaySelectedBattle(out var replayUnavailableReason);
        var canRecordSelectedBattle = CanRecordSelectedBattle(out _);
        var canDeleteSelectedRun = CanDeleteSelectedRun(out _);
        var visibleBattles =
            _sectionMode == HistorySectionMode.Ghost
                ? FilteredGhostBattles.ToList()
                : _battles.ToList();

        var selectedBattle = ActiveSelectedBattle;
        var hasSelectedBattle = selectedBattle != null;

        var detailResultText = hasSelectedBattle
            ? HistoryPanelFormatter.FormatBattleResult(selectedBattle!)
            : string.Empty;
        var detailResultSeverity = ResolveBattleResultSeverity(selectedBattle);
        var detailDayText = hasSelectedBattle
            ? HistoryPanelFormatter.FormatDayOnly(selectedBattle!.Day)
            : string.Empty;
        var detailOpponentName = hasSelectedBattle
            ? (selectedBattle!.OpponentName ?? HistoryPanelText.UnknownOpponent())
            : string.Empty;
        var detailMetaText = hasSelectedBattle
            ? HistoryPanelFormatter.FormatTimestamp(selectedBattle!.RecordedAtUtc)
            : string.Empty;
        var detailSnapshotText = hasSelectedBattle
            ? HistoryPanelFormatter.FormatSnapshotSummary(selectedBattle!.SnapshotCounts)
            : string.Empty;
        var detailPlaceholderText = hasSelectedBattle
            ? string.Empty
            : HistoryPanelText.SelectBattleForFooter();

        var ghostOpponentEliminatedNoticeText = HistoryPanelFormatter.IsGhostOpponentEliminated(
            selectedBattle
        )
            ? HistoryPanelText.GhostOpponentEliminatedNotice()
            : string.Empty;
        var serverHealthDisplay = _state.ServerHealthProbeInProgress
            ? HistoryPanelServerHealthFormatter.Checking()
            : HistoryPanelServerHealthFormatter.Idle();

        var statusSeverity = _state.StatusSeverity;
        var databaseChipSeverity = ResolveDatabaseChipSeverity();

        return new HistoryPanelUiToolkitModel
        {
            Title = HistoryPanelText.Title(),
            Subtitle = HistoryPanelText.Subtitle(),
            Supporters = _supporters,
            CountChipText =
                _sectionMode == HistorySectionMode.Ghost
                    ? HistoryPanelText.CountGhost(FilteredGhostBattles.Count)
                    : HistoryPanelText.CountRuns(_runs.Count),
            BattleChipText =
                _sectionMode == HistorySectionMode.Ghost
                    ? HistoryPanelText.CountBattles(FilteredGhostBattles.Count)
                    : HistoryPanelText.CountBattles(_battles.Count),
            DatabaseChipText = HistoryPanelText.DatabaseChip(GetDatabaseChipText()),
            DatabaseChipSeverity = databaseChipSeverity,
            ServerHealthButtonText = serverHealthDisplay.ButtonText,
            ServerHealthButtonEnabled = serverHealthDisplay.ButtonEnabled,
            SectionMode = _sectionMode,
            GhostBattleFilter = _ghostBattleFilter,
            StatusMessage = _statusMessage,
            StatusSeverity = statusSeverity,
            Runs = _runs,
            VisibleBattles = visibleBattles,
            SelectedRunIndex = _selectedRunIndex,
            SelectedBattleIndex =
                _sectionMode == HistorySectionMode.Ghost
                    ? _selectedGhostBattleIndex
                    : _selectedBattleIndex,
            RunsBattleSubtitle =
                SelectedRun == null
                    ? HistoryPanelText.SelectRunSubtitle()
                    : $"{SelectedRun.Hero} | {HistoryPanelFormatter.FormatDayOnly(SelectedRun.FinalDay)}",
            ReplayButtonText = _replayActionInProgress
                ? HistoryPanelText.Working()
                : GetReplayButtonLabel(
                    ActiveSelectedBattle,
                    canReplaySelectedBattle,
                    replayUnavailableReason
                ),
            ReplayButtonEnabled = canReplaySelectedBattle && !_replayActionInProgress,
            RecordAndReplayButtonText = HistoryPanelText.RecordAndReplay(),
            RecordAndReplayButtonEnabled = canRecordSelectedBattle && !_replayActionInProgress,
            DeleteButtonText = GetDeleteRunButtonLabel(
                _sectionMode == HistorySectionMode.Runs
                    && SelectedRun != null
                    && IsDeleteRunConfirmationActive(SelectedRun.RunId)
            ),
            DeleteButtonEnabled = canDeleteSelectedRun,
            HasSelectedBattle = hasSelectedBattle,
            DetailResultText = detailResultText,
            DetailResultSeverity = detailResultSeverity,
            DetailDayText = detailDayText,
            DetailOpponentName = detailOpponentName,
            DetailMetaText = detailMetaText,
            DetailSnapshotText = detailSnapshotText,
            DetailPlaceholderText = detailPlaceholderText,
            GhostOpponentEliminatedNoticeText = ghostOpponentEliminatedNoticeText,
        };
    }

    private static string GetDeleteRunButtonLabel(bool confirming)
    {
        return confirming ? HistoryPanelText.DeleteConfirm() : HistoryPanelText.Delete();
    }

    private string GetReplayButtonLabel(
        HistoryBattleRecord? battle,
        bool canReplaySelectedBattle,
        string replayUnavailableReason
    )
    {
        if (canReplaySelectedBattle)
            return _replayService.GetReplayActionLabel(battle);

        if (_runtime?.IsInGameRun == true)
            return HistoryPanelText.ReplayDisabledInRun();

        return string.IsNullOrWhiteSpace(replayUnavailableReason)
            ? _replayService.GetReplayActionLabel(battle)
            : HistoryPanelText.ReplayUnavailable();
    }

    // Connected -> Success(green); Missing (fresh install, File.Exists=false) -> Neutral, NOT an
    // error; Unavailable (repository uninitialized) -> Failure. Reads each flag once.
    private StatusSeverity ResolveDatabaseChipSeverity()
    {
        if (!_dataService.IsAvailable)
            return StatusSeverity.Failure;
        return _dataService.DatabaseExists ? StatusSeverity.Success : StatusSeverity.Neutral;
    }

    private static StatusSeverity ResolveBattleResultSeverity(HistoryBattleRecord? battle)
    {
        if (battle == null)
            return StatusSeverity.Neutral;

        // eliminated first (it also counts as a win)
        if (HistoryPanelFormatter.IsGhostOpponentEliminated(battle))
            return StatusSeverity.Confirm; // -> Eliminated accent pill

        if (HistoryPanelFormatter.IsBattleWin(battle))
            return StatusSeverity.Success;

        if (HistoryPanelFormatter.IsBattleLoss(battle))
            return StatusSeverity.Failure;

        return StatusSeverity.Neutral;
    }
}

internal sealed class HistoryPanelUiToolkitModel
{
    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public IReadOnlyList<BPPSupporterSample> Supporters { get; set; } =
        new List<BPPSupporterSample>();

    public string CountChipText { get; set; } = string.Empty;

    public string BattleChipText { get; set; } = string.Empty;

    public string DatabaseChipText { get; set; } = string.Empty;

    public string ServerHealthButtonText { get; set; } = string.Empty;

    public bool ServerHealthButtonEnabled { get; set; }

    public HistorySectionMode SectionMode { get; set; }

    public GhostBattleFilter GhostBattleFilter { get; set; }

    public string? StatusMessage { get; set; }

    public StatusSeverity StatusSeverity { get; set; }

    public StatusSeverity DatabaseChipSeverity { get; set; }

    public List<HistoryRunRecord> Runs { get; set; } = new();

    public List<HistoryBattleRecord> VisibleBattles { get; set; } = new();

    public int SelectedRunIndex { get; set; }

    public int SelectedBattleIndex { get; set; }

    public string RunsBattleSubtitle { get; set; } = string.Empty;

    public string ReplayButtonText { get; set; } = string.Empty;

    public bool ReplayButtonEnabled { get; set; }

    public string RecordAndReplayButtonText { get; set; } = string.Empty;

    public bool RecordAndReplayButtonEnabled { get; set; }

    public string DeleteButtonText { get; set; } = string.Empty;

    public bool DeleteButtonEnabled { get; set; }

    public bool HasSelectedBattle { get; set; }

    public string DetailResultText { get; set; } = string.Empty;

    public StatusSeverity DetailResultSeverity { get; set; }

    public string DetailDayText { get; set; } = string.Empty;

    public string DetailOpponentName { get; set; } = string.Empty;

    public string DetailMetaText { get; set; } = string.Empty;

    public string DetailSnapshotText { get; set; } = string.Empty;

    public string DetailPlaceholderText { get; set; } = string.Empty;

    public string GhostOpponentEliminatedNoticeText { get; set; } = string.Empty;
}
