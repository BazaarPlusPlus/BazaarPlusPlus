#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelCoordinator : IDisposable
{
    private readonly HistoryPanelState _state;
    private readonly IHistoryPanelRuntime _runtime;
    private readonly HistoryPanelDataService _dataService;
    private readonly HistoryPanelReplayService _replayService;
    private readonly Action _requestUiRefresh;
    private readonly Action _requestPreviewRefresh;
    private readonly Action<bool> _requestVisibilityChange;
    private CancellationTokenSource? _panelSessionCts;
    private int _panelSessionVersion;

    public HistoryPanelCoordinator(
        HistoryPanelState state,
        HistoryPanelDependencies dependencies,
        Action requestUiRefresh,
        Action requestPreviewRefresh,
        Action<bool> requestVisibilityChange
    )
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        if (dependencies == null)
            throw new ArgumentNullException(nameof(dependencies));
        _runtime = dependencies.Runtime;
        _dataService = dependencies.DataService;
        _replayService = dependencies.ReplayService;
        _requestUiRefresh =
            requestUiRefresh ?? throw new ArgumentNullException(nameof(requestUiRefresh));
        _requestPreviewRefresh =
            requestPreviewRefresh ?? throw new ArgumentNullException(nameof(requestPreviewRefresh));
        _requestVisibilityChange =
            requestVisibilityChange
            ?? throw new ArgumentNullException(nameof(requestVisibilityChange));
    }

    public void Dispose()
    {
        EndPanelSession();
    }

    public void OnPanelShown()
    {
        BeginPanelSession();
        _state.IsVisible = true;
        RefreshSectionOnEntry();
    }

    public void OnPanelHidden()
    {
        _state.IsVisible = false;
        ClearDeleteRunConfirmation();
        EndPanelSession();
    }

    public void Tick(float now)
    {
        if (
            string.IsNullOrWhiteSpace(_state.DeleteRunConfirmationRunId)
            || now < _state.DeleteRunConfirmationUntil
        )
            return;

        ClearDeleteRunConfirmation();
        if (_state.ShouldClearStatusWhenDeleteConfirmationExpires())
            _state.StatusMessage = null;
        _requestUiRefresh();
    }

    public void RefreshSectionOnEntry()
    {
        RefreshData();

        if (_state.SectionMode == HistorySectionMode.Ghost && _dataService.CanSyncGhostBattles)
            _ = TrySyncGhostBattlesAsync();
    }

    public void RefreshData()
    {
        ClearTransientStatus();
        ClearDeleteRunConfirmation();
        _state.Runs.Clear();
        _state.Battles.Clear();
        _state.GhostBattles.Clear();
        InvalidateFilteredGhostBattles();

        if (_state.SectionMode == HistorySectionMode.Ghost)
        {
            RefreshGhostData();
            return;
        }

        if (!_dataService.TryLoadRecentRuns(40, out var runs, out var statusMessage, out var error))
        {
            _state.StatusMessage = statusMessage;
            if (error != null)
            {
                BppLog.Error("HistoryPanel", "Failed to load history page data", error);
                _requestUiRefresh();
                _requestPreviewRefresh();
                return;
            }

            _requestUiRefresh();
            return;
        }

        _state.Runs.AddRange(runs);
        _state.SelectedRunIndex = Mathf.Clamp(
            _state.SelectedRunIndex,
            0,
            Mathf.Max(0, _state.Runs.Count - 1)
        );
        LoadBattlesForSelectedRun();
        _state.PreviewSelectionMode = PreviewSelectionMode.Run;
        _state.StatusMessage = statusMessage;

        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void RefreshGhostData()
    {
        ClearTransientStatus();
        _state.GhostBattles.Clear();
        InvalidateFilteredGhostBattles();
        if (
            !_dataService.TryLoadGhostBattles(
                100,
                out var battles,
                out var statusMessage,
                out var error
            )
        )
        {
            _state.StatusMessage = statusMessage;
            if (error != null)
            {
                BppLog.Error("HistoryPanel", "Failed to load ghost battle data", error);
                _requestUiRefresh();
                _requestPreviewRefresh();
                return;
            }

            _requestUiRefresh();
            return;
        }

        _state.GhostBattles.AddRange(battles);
        InvalidateFilteredGhostBattles();
        _state.SelectedGhostBattleIndex = Mathf.Clamp(
            _state.SelectedGhostBattleIndex,
            0,
            Mathf.Max(0, GetFilteredGhostBattles().Count - 1)
        );
        _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
        _state.StatusMessage = statusMessage;

        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void SetSectionMode(HistorySectionMode mode)
    {
        if (_state.SectionMode == mode)
            return;

        _state.SectionMode = mode;
        _state.PreviewSelectionMode =
            mode == HistorySectionMode.Ghost
                ? PreviewSelectionMode.Battle
                : PreviewSelectionMode.Run;
        RefreshSectionOnEntry();
    }

    public void SetGhostBattleFilter(GhostBattleFilter filter)
    {
        if (_state.GhostBattleFilter == filter)
            return;

        _state.GhostBattleFilter = filter;
        InvalidateFilteredGhostBattles();
        _state.SelectedGhostBattleIndex = Mathf.Clamp(
            _state.SelectedGhostBattleIndex,
            0,
            Mathf.Max(0, GetFilteredGhostBattles().Count - 1)
        );
        _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void SelectRun(int index)
    {
        if (index < 0 || index >= _state.Runs.Count)
            return;

        if (_state.SelectedRunIndex != index)
            ClearDeleteRunConfirmation();

        _state.SelectedRunIndex = index;
        LoadBattlesForSelectedRun();
        _state.PreviewSelectionMode = PreviewSelectionMode.Run;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void SelectBattle(int index)
    {
        var source =
            _state.SectionMode == HistorySectionMode.Ghost
                ? GetFilteredGhostBattles()
                : (IReadOnlyList<HistoryBattleRecord>)_state.Battles;
        if (index < 0 || index >= source.Count)
            return;

        if (_state.SectionMode == HistorySectionMode.Ghost)
            _state.SelectedGhostBattleIndex = index;
        else
            _state.SelectedBattleIndex = index;
        _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public bool CanReplaySelectedBattle(
        HistoryBattleRecord? activeSelectedBattle,
        out string reason
    )
    {
        return _replayService.CanReplayBattle(activeSelectedBattle, out reason);
    }

    public bool CanDeleteSelectedRun(HistoryRunRecord? selectedRun, out string reason)
    {
        if (_state.SectionMode == HistorySectionMode.Ghost)
        {
            reason = "Ghost battles cannot be deleted from this panel yet.";
            return false;
        }

        if (selectedRun == null)
        {
            reason = "Select a run to delete.";
            return false;
        }

        if (string.Equals(selectedRun.RawStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Active runs cannot be deleted.";
            return false;
        }

        if (
            _runtime.IsInGameRun
            && string.Equals(
                _runtime.CurrentServerRunId,
                selectedRun.RunId,
                StringComparison.Ordinal
            )
        )
        {
            reason = "The currently active gameplay run cannot be deleted.";
            return false;
        }

        if (!_dataService.IsAvailable)
        {
            reason = "Run log repository is unavailable.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public async Task TryReplaySelectedBattleAsync(HistoryBattleRecord? activeSelectedBattle)
    {
        var battle = activeSelectedBattle;
        if (battle == null)
            return;

        if (_state.ReplayActionInProgress)
        {
            _state.StatusMessage = "Replay action is already running.";
            _requestUiRefresh();
            return;
        }

        if (!CanReplaySelectedBattle(battle, out var replayUnavailableReason))
        {
            _state.StatusMessage = replayUnavailableReason;
            _requestUiRefresh();
            return;
        }

        _state.ReplayActionInProgress = true;
        _state.StatusMessage =
            battle.Source == HistoryBattleSource.Ghost && !battle.ReplayDownloaded
                ? "Downloading ghost replay..."
                : "Starting replay...";
        _requestUiRefresh();

        var token = GetCurrentSessionToken();
        var sessionVersion = _panelSessionVersion;
        HistoryPanelReplayAttemptResult replayResult;
        try
        {
            replayResult = await _replayService.ReplayBattleAsync(battle, token);
        }
        catch (OperationCanceledException)
        {
            if (!IsSessionCurrent(sessionVersion))
                return;

            _state.ReplayActionInProgress = false;
            _state.StatusMessage = null;
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!IsSessionCurrent(sessionVersion))
                return;

            _state.ReplayActionInProgress = false;
            _state.StatusMessage = $"Replay failed: {ex.Message}";
            BppLog.Error("HistoryPanel", "Failed to replay selected battle", ex);
            _requestUiRefresh();
            return;
        }

        if (!IsSessionCurrent(sessionVersion))
            return;

        _state.ReplayActionInProgress = false;
        _state.StatusMessage = replayResult.StatusMessage;
        if (!replayResult.Succeeded)
        {
            _requestUiRefresh();
            return;
        }

        _requestVisibilityChange(false);
    }

    public void TryDeleteSelectedRun(HistoryRunRecord? selectedRun)
    {
        var run = selectedRun;
        if (run == null)
            return;

        if (!CanDeleteSelectedRun(run, out var reason))
        {
            ClearDeleteRunConfirmation();
            _state.StatusMessage = reason;
            _requestUiRefresh();
            return;
        }

        if (!IsDeleteRunConfirmationActive(run.RunId))
        {
            _state.DeleteRunConfirmationRunId = run.RunId;
            _state.DeleteRunConfirmationUntil = Time.unscaledTime + 5f;
            _state.StatusMessage =
                $"Click Delete Run again within 5s to remove {HistoryPanelFormatter.ShortenRunId(run.RunId)}.";
            _requestUiRefresh();
            return;
        }

        ClearDeleteRunConfirmation();

        if (!_dataService.TryDeleteRun(run.RunId, out var battleIds, out var error))
        {
            _state.StatusMessage = $"Run delete failed: {error?.Message ?? "Unknown error"}";
            BppLog.Error(
                "HistoryPanel",
                $"Failed to delete run {run.RunId}",
                error ?? new InvalidOperationException("Unknown run delete failure.")
            );
            _requestUiRefresh();
            return;
        }

        _replayService.CleanupReplayPayloads(battleIds);
        var deletedMessage =
            battleIds.Count > 0
                ? $"Deleted run {HistoryPanelFormatter.ShortenRunId(run.RunId)} and cleaned {battleIds.Count} linked battle records."
                : $"Deleted run {HistoryPanelFormatter.ShortenRunId(run.RunId)}.";
        RefreshData();
        _state.StatusMessage = deletedMessage;
        _requestUiRefresh();
    }

    public string GetDatabaseChipText()
    {
        if (!_dataService.IsAvailable)
            return "Unavailable";

        return _dataService.DatabaseExists ? "Connected" : "Missing";
    }

    public async Task TrySyncGhostBattlesAsync()
    {
        if (_state.GhostSyncInProgress)
        {
            _state.StatusMessage = "Ghost sync is already running.";
            _requestUiRefresh();
            return;
        }

        if (!_dataService.CanSyncGhostBattles)
        {
            _state.StatusMessage = "Ghost sync is unavailable.";
            _requestUiRefresh();
            return;
        }

        _state.GhostSyncInProgress = true;
        _state.StatusMessage = "Syncing ghost battles...";
        _requestUiRefresh();

        var token = GetCurrentSessionToken();
        var sessionVersion = _panelSessionVersion;
        HistoryPanelGhostSyncAttemptResult syncResult;
        try
        {
            syncResult = await _dataService.SyncGhostBattlesAsync(token);
        }
        catch (OperationCanceledException)
        {
            if (!IsSessionCurrent(sessionVersion))
                return;

            _state.GhostSyncInProgress = false;
            _state.StatusMessage = null;
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!IsSessionCurrent(sessionVersion))
                return;

            _state.GhostSyncInProgress = false;
            _state.StatusMessage = $"Ghost sync failed: {ex.Message}";
            BppLog.Error("HistoryPanel", "Failed to sync ghost battles", ex);
            _requestUiRefresh();
            return;
        }

        if (!IsSessionCurrent(sessionVersion))
            return;

        _state.GhostSyncInProgress = false;
        _state.StatusMessage = syncResult.StatusMessage;
        if (!syncResult.Succeeded)
        {
            if (syncResult.Error != null)
                BppLog.Error("HistoryPanel", "Failed to sync ghost battles", syncResult.Error);
            _requestUiRefresh();
            return;
        }

        if (_state.SectionMode == HistorySectionMode.Ghost)
        {
            RefreshGhostData();
            _state.StatusMessage = syncResult.StatusMessage;
            _requestUiRefresh();
        }
        else
            _requestUiRefresh();
    }

    public IReadOnlyList<HistoryBattleRecord> GetFilteredGhostBattles()
    {
        if (!_state.FilteredGhostBattlesDirty)
            return _state.FilteredGhostBattles;

        _state.FilteredGhostBattles.Clear();
        foreach (var battle in _state.GhostBattles)
        {
            if (MatchesGhostFilter(battle))
                _state.FilteredGhostBattles.Add(battle);
        }

        _state.FilteredGhostBattlesDirty = false;
        return _state.FilteredGhostBattles;
    }

    public bool IsDeleteRunConfirmationActive(string runId)
    {
        return !string.IsNullOrWhiteSpace(runId)
            && string.Equals(_state.DeleteRunConfirmationRunId, runId, StringComparison.Ordinal)
            && Time.unscaledTime < _state.DeleteRunConfirmationUntil;
    }

    private void LoadBattlesForSelectedRun()
    {
        _state.Battles.Clear();
        _state.SelectedBattleIndex = 0;

        var run = GetSelectedRun();
        if (
            _dataService.TryLoadBattles(run?.RunId, out var battles, out var error)
            && battles.Count > 0
        )
            _state.Battles.AddRange(battles);

        if (error != null && run != null)
        {
            _state.StatusMessage = $"Battle load failed: {error.Message}";
            BppLog.Error("HistoryPanel", $"Failed to load battles for run {run.RunId}", error);
        }
    }

    private HistoryRunRecord? GetSelectedRun()
    {
        return _state.Runs.Count == 0
            ? null
            : _state.Runs[Mathf.Clamp(_state.SelectedRunIndex, 0, _state.Runs.Count - 1)];
    }

    private void ClearDeleteRunConfirmation()
    {
        _state.DeleteRunConfirmationRunId = null;
        _state.DeleteRunConfirmationUntil = 0f;
    }

    private void ClearTransientStatus()
    {
        if (!_state.ReplayActionInProgress && !_state.GhostSyncInProgress)
            _state.StatusMessage = null;
    }

    private void InvalidateFilteredGhostBattles()
    {
        _state.FilteredGhostBattlesDirty = true;
    }

    private bool MatchesGhostFilter(HistoryBattleRecord battle)
    {
        var outcome = ResolveGhostBattleOutcome(battle);
        return _state.GhostBattleFilter switch
        {
            GhostBattleFilter.IWon => outcome == GhostBattleOutcome.Won,
            GhostBattleFilter.ILost => outcome == GhostBattleOutcome.Lost,
            _ => true,
        };
    }

    private static GhostBattleOutcome ResolveGhostBattleOutcome(HistoryBattleRecord battle)
    {
        if (string.Equals(battle.WinnerCombatantId, "Player", StringComparison.OrdinalIgnoreCase))
            return GhostBattleOutcome.Won;

        if (string.Equals(battle.WinnerCombatantId, "Opponent", StringComparison.OrdinalIgnoreCase))
            return GhostBattleOutcome.Lost;

        var result = battle.Result?.Trim();
        if (
            string.Equals(result, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result, "Won", StringComparison.OrdinalIgnoreCase)
        )
            return GhostBattleOutcome.Won;

        if (
            string.Equals(result, "Loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result, "Lost", StringComparison.OrdinalIgnoreCase)
        )
            return GhostBattleOutcome.Lost;

        return GhostBattleOutcome.Unknown;
    }

    private enum GhostBattleOutcome
    {
        Unknown,
        Won,
        Lost,
    }

    private void BeginPanelSession()
    {
        EndPanelSession();
        _panelSessionVersion++;
        _panelSessionCts = new CancellationTokenSource();
    }

    private void EndPanelSession()
    {
        _panelSessionVersion++;
        if (_panelSessionCts == null)
            return;

        try
        {
            _panelSessionCts.Cancel();
        }
        catch
        {
            // Cancellation is best-effort during teardown.
        }

        _panelSessionCts.Dispose();
        _panelSessionCts = null;
    }

    private CancellationToken GetCurrentSessionToken()
    {
        return _panelSessionCts?.Token ?? CancellationToken.None;
    }

    private bool IsSessionCurrent(int version)
    {
        return version == _panelSessionVersion;
    }
}
