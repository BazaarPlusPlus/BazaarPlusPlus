#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanel
{
    private HistoryPanelUiToolkitView? _uiView;

    private void EnsureUi()
    {
        _uiView ??= new HistoryPanelUiToolkitView(
            transform,
            () => SetHistoryVisible(false),
            TryReplaySelectedBattle,
            TryDeleteSelectedRun,
            TryRefreshFinalBuilds,
            SelectRun,
            SelectBattle,
            SetSectionMode,
            SetGhostBattleFilter
        );
        _uiView.EnsureCreated();
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

    private void UpdatePreviewUiTick(bool previewDebugVisible)
    {
        if (_uiView == null)
            return;

        _uiView.SetPreviewTexture(_previewRenderer?.CurrentTexture);
        _uiView.SetPreviewDebugVisible(previewDebugVisible);
    }

    private void SetPreviewStatus(string? message, bool visible)
    {
        _uiView?.SetPreviewStatus(message, visible);
    }

    private void SetPreviewDebugText(string? message, bool visible)
    {
        _uiView?.SetPreviewDebug(message, visible);
    }

    private HistoryPanelUiToolkitModel BuildUiModel()
    {
        var canReplaySelectedBattle = CanReplaySelectedBattle(out var replayUnavailableReason);
        var canDeleteSelectedRun = CanDeleteSelectedRun(out _);
        var visibleBattles =
            _sectionMode == HistorySectionMode.Ghost
                ? FilteredGhostBattles.ToList()
                : _battles.ToList();

        var footerPrimaryText =
            ActiveSelectedBattle == null
                ? HistoryPanelText.NoBattleSelected()
                : $"{HistoryPanelFormatter.FormatBattleResult(ActiveSelectedBattle)} | {HistoryPanelFormatter.FormatDayOnly(ActiveSelectedBattle.Day)} | {ActiveSelectedBattle.OpponentName ?? HistoryPanelText.UnknownOpponent()}";

        var selectedBattleTimestamp =
            ActiveSelectedBattle == null
                ? null
                : HistoryPanelFormatter.FormatTimestamp(ActiveSelectedBattle.RecordedAtUtc);
        var selectedBattleTimestampText = selectedBattleTimestamp ?? string.Empty;
        var battleSummary =
            ActiveSelectedBattle == null ? HistoryPanelText.SelectBattleForFooter()
            : string.IsNullOrWhiteSpace(ActiveSelectedBattle.SnapshotSummary)
                ? selectedBattleTimestampText
            : $"{selectedBattleTimestampText} | {ActiveSelectedBattle.SnapshotSummary}";
        var footerSecondaryText = string.IsNullOrWhiteSpace(_statusMessage)
            ? battleSummary
            : $"{_statusMessage} | {battleSummary}";
        var ghostOpponentEliminatedNoticeText = HistoryPanelFormatter.IsGhostOpponentEliminated(
            ActiveSelectedBattle
        )
            ? HistoryPanelText.GhostOpponentEliminatedNotice()
            : string.Empty;

        return new HistoryPanelUiToolkitModel
        {
            Title = HistoryPanelText.Title(),
            Subtitle = HistoryPanelText.Subtitle(),
            CountChipText =
                _sectionMode == HistorySectionMode.Ghost
                    ? HistoryPanelText.CountGhost(FilteredGhostBattles.Count)
                    : HistoryPanelText.CountRuns(_runs.Count),
            BattleChipText =
                _sectionMode == HistorySectionMode.Ghost
                    ? HistoryPanelText.CountBattles(FilteredGhostBattles.Count)
                    : HistoryPanelText.CountBattles(_battles.Count),
            DatabaseChipText = HistoryPanelText.DatabaseChip(GetDatabaseChipText()),
            SectionMode = _sectionMode,
            GhostBattleFilter = _ghostBattleFilter,
            StatusMessage = _statusMessage,
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
            DeleteButtonText = GetDeleteRunButtonLabel(
                _sectionMode == HistorySectionMode.Runs
                    && SelectedRun != null
                    && IsDeleteRunConfirmationActive(SelectedRun.RunId)
            ),
            DeleteButtonEnabled = canDeleteSelectedRun,
            FinalBuildRefreshButtonText = _state.FinalBuildRefreshInProgress
                ? HistoryPanelText.Working()
                : HistoryPanelText.RefreshFinalBuilds(),
            FinalBuildRefreshButtonEnabled = !_state.FinalBuildRefreshInProgress,
            FooterPrimaryText = footerPrimaryText,
            FooterSecondaryText = footerSecondaryText,
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
}

internal sealed class HistoryPanelUiToolkitModel
{
    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string CountChipText { get; set; } = string.Empty;

    public string BattleChipText { get; set; } = string.Empty;

    public string DatabaseChipText { get; set; } = string.Empty;

    public HistorySectionMode SectionMode { get; set; }

    public GhostBattleFilter GhostBattleFilter { get; set; }

    public string? StatusMessage { get; set; }

    public List<HistoryRunRecord> Runs { get; set; } = new();

    public List<HistoryBattleRecord> VisibleBattles { get; set; } = new();

    public int SelectedRunIndex { get; set; }

    public int SelectedBattleIndex { get; set; }

    public string RunsBattleSubtitle { get; set; } = string.Empty;

    public string ReplayButtonText { get; set; } = string.Empty;

    public bool ReplayButtonEnabled { get; set; }

    public string DeleteButtonText { get; set; } = string.Empty;

    public bool DeleteButtonEnabled { get; set; }

    public string FinalBuildRefreshButtonText { get; set; } = string.Empty;

    public bool FinalBuildRefreshButtonEnabled { get; set; }

    public string FooterPrimaryText { get; set; } = string.Empty;

    public string FooterSecondaryText { get; set; } = string.Empty;

    public string GhostOpponentEliminatedNoticeText { get; set; } = string.Empty;
}
