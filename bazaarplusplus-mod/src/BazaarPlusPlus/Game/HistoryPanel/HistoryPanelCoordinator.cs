#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi.Clients;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelCoordinator : IDisposable
{
    private readonly HistoryPanelState _state;
    private readonly IHistoryPanelRuntime _runtime;
    private readonly HistoryPanelDataService _dataService;
    private readonly HistoryPanelReplayService _replayService;
    private readonly IHistoryPanelServerHealthProbe? _serverHealthProbe;
    private readonly Action _requestUiRefresh;
    private readonly Action _requestPreviewRefresh;
    private readonly Action<bool> _requestVisibilityChange;
    private readonly HistoryPanelSessionScope _session = new();

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
        _serverHealthProbe = dependencies.ServerHealthProbe;
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
        _session.Dispose();
    }

    public void OnPanelShown()
    {
        _session.Begin();
        _state.ReplayActionInProgress = false;
        _state.IsVisible = true;
        RefreshSectionOnEntry();
    }

    public void OnPanelHidden()
    {
        _state.IsVisible = false;
        _state.GhostSyncInProgress = false;
        _state.ReplayActionInProgress = false;
        _state.ServerHealthProbeInProgress = false;
        ClearDeleteRunConfirmation();
        _session.End();
    }

    public void Tick(float now)
    {
        if (
            string.IsNullOrWhiteSpace(_state.DeleteRunConfirmationRunId)
            || now < _state.DeleteRunConfirmationUntil
        )
            return;

        var shouldClearStatus = _state.ShouldClearStatusWhenDeleteConfirmationExpires();
        ClearDeleteRunConfirmation();
        if (shouldClearStatus)
            SetStatusMessage(null);
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
            SetStatusMessage(statusMessage);
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
        SetStatusMessage(statusMessage);

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
            SetStatusMessage(statusMessage);
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
        SetStatusMessage(statusMessage);

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

    public bool CanRecordSelectedBattle(
        HistoryBattleRecord? activeSelectedBattle,
        out string reason
    )
    {
        return _replayService.CanRecordReplay(activeSelectedBattle, out reason);
    }

    public void PrewarmRecordingAvailability()
    {
        _replayService.PrewarmRecordingAvailability();
    }

    public bool CanDeleteSelectedRun(HistoryRunRecord? selectedRun, out string reason)
    {
        if (_state.SectionMode == HistorySectionMode.Ghost)
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
            _runtime.IsInGameRun
            && string.Equals(
                _runtime.CurrentServerRunId,
                selectedRun.RunId,
                StringComparison.Ordinal
            )
        )
        {
            reason = HistoryPanelText.CurrentGameplayRunDeleteUnavailable();
            return false;
        }

        if (!_dataService.IsAvailable)
        {
            reason = HistoryPanelText.RunLogRepositoryUnavailable();
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public async Task TryReplaySelectedBattleAsync(
        HistoryBattleRecord? activeSelectedBattle,
        bool recordVideo
    )
    {
        var battle = activeSelectedBattle;
        if (battle == null)
            return;

        if (_state.ReplayActionInProgress)
        {
            SetStatusMessage(HistoryPanelText.ReplayActionAlreadyRunning());
            _requestUiRefresh();
            return;
        }

        if (!CanReplaySelectedBattle(battle, out var replayUnavailableReason))
        {
            SetStatusMessage(replayUnavailableReason);
            _requestUiRefresh();
            return;
        }

        // Recording must be feasible before a record-and-replay request proceeds; otherwise we
        // surface the reason and refuse rather than silently starting a no-video replay.
        if (recordVideo)
        {
            var canRecord = CanRecordSelectedBattle(battle, out var recordUnavailableReason);
            BppLog.Info(
                "HistoryPanel",
                $"Record-and-replay requested battle={battle.BattleId} canRecord={canRecord}"
                    + (canRecord ? string.Empty : $" reason={recordUnavailableReason}")
            );
            if (!canRecord)
            {
                SetStatusMessage(recordUnavailableReason);
                _requestUiRefresh();
                return;
            }
        }

        _state.ReplayActionInProgress = true;
        SetStatusMessage(
            battle.Source == HistoryBattleSource.Ghost && !battle.ReplayDownloaded
                ? HistoryPanelText.DownloadingGhostReplay()
                : HistoryPanelText.StartingReplay(),
            StatusSeverity.Pending
        );
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        HistoryPanelReplayAttemptResult replayResult;
        try
        {
            replayResult = await _replayService.ReplayBattleAsync(
                battle,
                recordVideo,
                _session.Token
            );
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.ReplayActionInProgress = false;
            SetStatusMessage(null);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.ReplayActionInProgress = false;
            SetStatusMessage(HistoryPanelText.ReplayFailed(ex.Message), StatusSeverity.Failure);
            BppLog.Error("HistoryPanel", "Failed to replay selected battle", ex);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.ReplayActionInProgress = false;
        SetStatusMessage(
            replayResult.StatusMessage,
            replayResult.Succeeded ? StatusSeverity.Success : StatusSeverity.Failure
        );
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
            SetStatusMessage(reason);
            _requestUiRefresh();
            return;
        }

        if (!IsDeleteRunConfirmationActive(run.RunId))
        {
            _state.DeleteRunConfirmationRunId = run.RunId;
            _state.DeleteRunConfirmationUntil = Time.unscaledTime + 5f;
            SetStatusMessage(
                HistoryPanelText.DeleteRunConfirm(HistoryPanelFormatter.ShortenRunId(run.RunId)),
                isDeleteConfirmation: true
            );
            _requestUiRefresh();
            return;
        }

        ClearDeleteRunConfirmation();

        if (!_dataService.TryDeleteRun(run.RunId, out var battleIds, out var error))
        {
            SetStatusMessage(
                HistoryPanelText.RunDeleteFailed(error?.Message ?? HistoryPanelText.Unknown()),
                StatusSeverity.Failure
            );
            BppLog.Error(
                "HistoryPanel",
                $"Failed to delete run {run.RunId}",
                error ?? new InvalidOperationException("Unknown run delete failure.")
            );
            _requestUiRefresh();
            return;
        }

        _replayService.CleanupReplayPayloads(battleIds);
        var deletedMessage = HistoryPanelText.DeletedRun(
            HistoryPanelFormatter.ShortenRunId(run.RunId),
            battleIds.Count
        );
        RefreshData();
        SetStatusMessage(deletedMessage, StatusSeverity.Success);
        _requestUiRefresh();
    }

    public string GetDatabaseChipText()
    {
        if (!_dataService.IsAvailable)
            return HistoryPanelText.DatabaseUnavailable();

        return _dataService.DatabaseExists
            ? HistoryPanelText.DatabaseConnected()
            : HistoryPanelText.DatabaseMissing();
    }

    public async Task TryCheckServerHealthAsync()
    {
        if (_state.ServerHealthProbeInProgress)
        {
            SetStatusMessage(HistoryPanelText.ServerHealthAlreadyRunning());
            _requestUiRefresh();
            return;
        }

        if (_serverHealthProbe == null)
        {
            var unavailable = HistoryPanelServerHealthFormatter.Unavailable();
            SetStatusMessage(unavailable.StatusMessage);
            _requestUiRefresh();
            return;
        }

        _state.ServerHealthProbeInProgress = true;
        var checking = HistoryPanelServerHealthFormatter.Checking();
        SetStatusMessage(checking.StatusMessage, StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        ModApiHealthProbeResult result;
        try
        {
            result = await _serverHealthProbe.ProbeAsync(_session.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.ServerHealthProbeInProgress = false;
            SetStatusMessage(null);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.ServerHealthProbeInProgress = false;
            SetStatusMessage(
                HistoryPanelText.ServerHealthFailed(0, ex.Message),
                StatusSeverity.Failure
            );
            BppLog.Error("HistoryPanel", "Failed to check server health", ex);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.ServerHealthProbeInProgress = false;
        var display = HistoryPanelServerHealthFormatter.FromProbeResult(result);
        SetStatusMessage(
            display.StatusMessage,
            result.Succeeded ? StatusSeverity.Success : StatusSeverity.Failure
        );
        if (result.Succeeded)
        {
            BppLog.Info(
                "HistoryPanel",
                $"Server health check succeeded rttMs={result.RoundTripMilliseconds}"
            );
        }
        else
        {
            BppLog.Warn(
                "HistoryPanel",
                $"Server health check failed rttMs={result.RoundTripMilliseconds} error={result.Error}"
            );
        }
        _requestUiRefresh();
    }

    public async Task TrySyncGhostBattlesAsync()
    {
        if (_state.GhostSyncInProgress)
        {
            SetStatusMessage(HistoryPanelText.GhostSyncAlreadyRunning());
            _requestUiRefresh();
            return;
        }

        if (!_dataService.CanSyncGhostBattles)
        {
            SetStatusMessage(HistoryPanelText.GhostSyncUnavailable());
            _requestUiRefresh();
            return;
        }

        _state.GhostSyncInProgress = true;
        SetStatusMessage(HistoryPanelText.SyncingGhostBattles(), StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        HistoryPanelAttemptResult syncResult;
        try
        {
            syncResult = await _dataService.SyncGhostBattlesAsync(_session.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.GhostSyncInProgress = false;
            SetStatusMessage(null);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;

            _state.GhostSyncInProgress = false;
            SetStatusMessage(HistoryPanelText.GhostSyncFailed(ex.Message), StatusSeverity.Failure);
            BppLog.Error("HistoryPanel", "Failed to sync ghost battles", ex);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.GhostSyncInProgress = false;
        SetStatusMessage(
            syncResult.StatusMessage,
            syncResult.Succeeded ? StatusSeverity.Success : StatusSeverity.Failure
        );
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
            SetStatusMessage(syncResult.StatusMessage, StatusSeverity.Success);
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
            if (HistoryPanelGhostBattleFilter.Matches(_state.GhostBattleFilter, battle))
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
            SetStatusMessage(HistoryPanelText.BattleLoadFailed(error.Message));
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
        _state.DeleteRunConfirmationStatusActive = false;
    }

    private void ClearTransientStatus()
    {
        if (
            !_state.ReplayActionInProgress
            && !_state.GhostSyncInProgress
            && !_state.ServerHealthProbeInProgress
        )
            SetStatusMessage(null);
    }

    private void SetStatusMessage(
        string? statusMessage,
        StatusSeverity severity = StatusSeverity.Neutral,
        bool isDeleteConfirmation = false
    )
    {
        _state.StatusMessage = statusMessage;
        _state.DeleteRunConfirmationStatusActive =
            isDeleteConfirmation && !string.IsNullOrWhiteSpace(statusMessage);
        // Severity travels with the message so the banner colour can't desync from in-flight flags
        // (the source of the phase-1 Pending timing coupling). An empty message clears to Neutral;
        // a delete confirmation always reads as Confirm regardless of the caller's severity.
        _state.StatusSeverity =
            string.IsNullOrWhiteSpace(statusMessage) ? StatusSeverity.Neutral
            : isDeleteConfirmation ? StatusSeverity.Confirm
            : severity;
    }

    private void InvalidateFilteredGhostBattles()
    {
        _state.FilteredGhostBattlesDirty = true;
    }

    // Kept as a thin alias on the coordinator so external test reflection that targets
    // HistoryPanelCoordinator+GhostBattleOutcome / ResolveGhostBattleOutcome continues to compile.
    // The actual matching logic lives in HistoryPanelGhostBattleFilter.
    private static GhostBattleOutcome ResolveGhostBattleOutcome(HistoryBattleRecord battle)
    {
        return HistoryPanelGhostBattleFilter.ResolveOutcomeForCompatibility(battle) switch
        {
            HistoryPanelGhostBattleOutcome.Won => GhostBattleOutcome.Won,
            HistoryPanelGhostBattleOutcome.Lost => GhostBattleOutcome.Lost,
            _ => GhostBattleOutcome.Unknown,
        };
    }

    private enum GhostBattleOutcome
    {
        Unknown,
        Won,
        Lost,
    }
}
