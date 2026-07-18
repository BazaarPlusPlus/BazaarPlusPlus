#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.GameInterop;
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
    private readonly BazaarDbLinkClient? _linkClient;
    private readonly BazaarDbAccountLinkStore _accountLinkStore = new();
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
        _linkClient = dependencies.AccountLinkClient;
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
        _state.AccountLinkExpanded = false;
        // Begin() cancelled any in-flight redeem (its continuations bail on !IsCurrent without
        // resetting state), and re-entrant opens skip OnPanelHidden — reset here or the toggle
        // guard leaves the account-link row permanently inert.
        _state.AccountLinkInProgress = false;
        RefreshAccountLinkIdentityFromGame();
        _state.ReplayActionInProgress = false;
        RefreshSectionOnEntry();
    }

    public void OnPanelHidden()
    {
        _state.GhostSyncInProgress = false;
        _state.ReplayActionInProgress = false;
        _state.ServerHealthProbeInProgress = false;
        _state.AccountLinkInProgress = false;
        ClearDeleteRunConfirmation();
        _session.End();
    }

    public void Tick(float now)
    {
        if (!_state.DeleteRunConfirmation.HasExpired(now))
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
        InvalidateFilteredRuns();
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
                BppLog.ErrorEvent(
                    HistoryPanelLogEvents.DataLoadFailed,
                    error,
                    HistoryPanelLogEvents.DataDataset.Bind(HistoryPanelDataset.RecentRuns)
                );
                _requestUiRefresh();
                _requestPreviewRefresh();
                return;
            }

            _requestUiRefresh();
            return;
        }

        _state.Runs.AddRange(runs);
        InvalidateFilteredRuns();
        _state.SelectedRunIndex = ClampIndex(_state.SelectedRunIndex, GetFilteredRuns().Count);
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
                BppLog.ErrorEvent(
                    HistoryPanelLogEvents.DataLoadFailed,
                    error,
                    HistoryPanelLogEvents.DataDataset.Bind(HistoryPanelDataset.GhostBattles)
                );
                _requestUiRefresh();
                _requestPreviewRefresh();
                return;
            }

            _requestUiRefresh();
            return;
        }

        _state.GhostBattles.AddRange(battles);
        InvalidateFilteredGhostBattles();
        _state.SelectedGhostBattleIndex = ClampIndex(
            _state.SelectedGhostBattleIndex,
            GetFilteredGhostBattles().Count
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
        _state.SelectedGhostBattleIndex = ClampIndex(
            _state.SelectedGhostBattleIndex,
            GetFilteredGhostBattles().Count
        );
        _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void SetRunHeroFilter(string hero)
    {
        var selectedHero = string.IsNullOrEmpty(hero) ? null : hero;
        _state.SelectedRunHero =
            selectedHero != null
            && !string.Equals(
                _state.SelectedRunHero,
                selectedHero,
                StringComparison.OrdinalIgnoreCase
            )
                ? selectedHero
                : null;
        InvalidateFilteredRuns();
        _state.SelectedRunIndex = 0;
        ClearDeleteRunConfirmation();
        LoadBattlesForSelectedRun();
        _state.PreviewSelectionMode = PreviewSelectionMode.Run;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void ToggleGhostDayMin10()
    {
        SetGhostDayMin10(!_state.GhostDayMin10);
    }

    public void SetGhostDayMin10(bool value)
    {
        if (_state.GhostDayMin10 == value)
            return;

        _state.GhostDayMin10 = value;
        InvalidateFilteredGhostBattles();
        _state.SelectedGhostBattleIndex = ClampIndex(
            _state.SelectedGhostBattleIndex,
            GetFilteredGhostBattles().Count
        );
        _state.PreviewSelectionMode = PreviewSelectionMode.Battle;
        _requestUiRefresh();
        _requestPreviewRefresh();
    }

    public void SelectRun(int index)
    {
        var filteredRuns = GetFilteredRuns();
        if (index < 0 || index >= filteredRuns.Count)
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
        return HistoryPanelDecisions.CanDeleteRun(
            _state.SectionMode,
            selectedRun,
            _runtime.IsInGameRun,
            _runtime.CurrentServerRunId,
            _dataService.IsAvailable,
            out reason
        );
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

        var logOperation = new HistoryPanelReplayLogOperation(
            Guid.NewGuid().ToString("N"),
            battle.BattleId,
            recordVideo
        );

        // Recording must be feasible before a record-and-replay request proceeds; otherwise we
        // surface the reason and refuse rather than silently starting a no-video replay.
        if (recordVideo)
        {
            var canRecord = _replayService.CanRecordReplay(
                battle,
                out var recordUnavailableReason,
                out var recordingReasonCode
            );
            if (
                logOperation.TryRecordPreflight(
                    canRecord,
                    recordingReasonCode,
                    out var preflightResult
                )
            )
                HistoryPanelLogWriter.EmitReplayPreflight(preflightResult);
            if (!canRecord)
            {
                SetStatusMessage(recordUnavailableReason);
                if (logOperation.TryFail(recordingReasonCode, exception: null, out var failure))
                    HistoryPanelLogWriter.EmitReplayFailed(failure);
                _requestUiRefresh();
                return;
            }
        }

        _state.ReplayActionInProgress = true;
        var sessionVersion = _session.Version;
        SetStatusMessage(
            battle.Source == HistoryBattleSource.Ghost && !battle.ReplayDownloaded
                ? HistoryPanelText.DownloadingGhostReplay()
                : HistoryPanelText.StartingReplay(),
            StatusSeverity.Pending
        );
        HistoryPanelReplayAttemptResult replayResult;
        try
        {
            _requestUiRefresh();
            replayResult = await _replayService.ReplayBattleAsync(
                battle,
                recordVideo,
                _session.Token
            );
        }
        catch (OperationCanceledException ex)
        {
            var cancellation = HistoryPanelCancellationRouter.Resolve(
                _session.IsCurrent(sessionVersion)
            );
            if (cancellation == HistoryPanelCancellationDisposition.AbandonStaleRequest)
            {
                logOperation.Abandon();
                return;
            }

            _state.ReplayActionInProgress = false;
            if (logOperation.TryFail(HistoryPanelReplayReasonCode.Canceled, ex, out var failure))
                HistoryPanelLogWriter.EmitReplayFailed(failure);
            SetStatusMessage(HistoryPanelText.ReplayFailed(ex.Message), StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
            {
                logOperation.Abandon();
                return;
            }

            _state.ReplayActionInProgress = false;
            SetStatusMessage(HistoryPanelText.ReplayFailed(ex.Message), StatusSeverity.Failure);
            if (
                logOperation.TryFail(
                    HistoryPanelReplayReasonCode.UnexpectedException,
                    ex,
                    out var failure
                )
            )
                HistoryPanelLogWriter.EmitReplayFailed(failure);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
        {
            logOperation.Abandon();
            return;
        }

        _state.ReplayActionInProgress = false;
        if (!replayResult.Succeeded)
        {
            if (
                logOperation.TryFail(
                    replayResult.ReasonCode,
                    replayResult.Exception,
                    out var failure
                )
            )
                HistoryPanelLogWriter.EmitReplayFailed(failure);
            SetStatusMessage(replayResult.StatusMessage, StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }

        if (logOperation.TryAccept(out var accepted))
            HistoryPanelLogWriter.EmitReplayAccepted(accepted);
        SetStatusMessage(replayResult.StatusMessage, StatusSeverity.Success);
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

        var now = Time.unscaledTime;
        if (!IsDeleteRunConfirmationActive(run.RunId, now))
        {
            _state.DeleteRunConfirmation = new DeleteConfirmation(run.RunId, now + 5f);
            SetStatusMessage(
                HistoryPanelText.DeleteRunConfirm(HistoryPanelFormatter.ShortenRunId(run.RunId)),
                isDeleteConfirmation: true
            );
            _requestUiRefresh();
            return;
        }

        ClearDeleteRunConfirmation();

        var logOperation = new HistoryPanelRunDeleteLogOperation(
            Guid.NewGuid().ToString("N"),
            run.RunId
        );

        if (!_dataService.TryDeleteRun(run.RunId, out var battleIds, out var error))
        {
            SetStatusMessage(
                HistoryPanelText.RunDeleteFailed(error?.Message ?? HistoryPanelText.Unknown()),
                StatusSeverity.Failure
            );
            if (
                logOperation.TryComplete(
                    HistoryPanelRunDeleteTerminalStatus.Failed,
                    battleIds.Count,
                    cleanupFailedCount: 0,
                    HistoryPanelRunDeleteReasonCode.PrimaryDeleteFailed,
                    error ?? new InvalidOperationException("Unknown run delete failure."),
                    out var failed
                )
            )
                HistoryPanelLogWriter.EmitRunDeleteTerminal(failed);
            _requestUiRefresh();
            return;
        }

        var cleanupResult = _replayService.CleanupReplayPayloads(battleIds);
        if (cleanupResult.FailedBattleCount > 0)
        {
            if (
                logOperation.TryComplete(
                    HistoryPanelRunDeleteTerminalStatus.Degraded,
                    battleIds.Count,
                    cleanupResult.FailedBattleCount,
                    HistoryPanelRunDeleteReasonCode.ReplayPayloadCleanupFailed,
                    cleanupResult.Exception,
                    out var degraded
                )
            )
                HistoryPanelLogWriter.EmitRunDeleteTerminal(degraded);
        }
        else if (
            logOperation.TryComplete(
                HistoryPanelRunDeleteTerminalStatus.Succeeded,
                battleIds.Count,
                cleanupFailedCount: 0,
                HistoryPanelRunDeleteReasonCode.Completed,
                exception: null,
                out var succeeded
            )
        )
            HistoryPanelLogWriter.EmitRunDeleteTerminal(succeeded);
        var deletedMessage = HistoryPanelText.DeletedRun(
            HistoryPanelFormatter.ShortenRunId(run.RunId),
            battleIds.Count
        );
        RefreshData();
        SetStatusMessage(deletedMessage, StatusSeverity.Success);
        _requestUiRefresh();
    }

    public HistoryPanelDatabaseChip ResolveDatabaseChip()
    {
        return HistoryPanelDecisions.ResolveDatabaseChip(
            _dataService.IsAvailable,
            _dataService.DatabaseExists
        );
    }

    public string GetReplayActionLabel(HistoryBattleRecord? battle)
    {
        return _replayService.GetReplayActionLabel(battle);
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

        var logOperation = new HistoryPanelServerHealthLogOperation(Guid.NewGuid().ToString("N"));

        _state.ServerHealthProbeInProgress = true;
        var sessionVersion = _session.Version;
        var checking = HistoryPanelServerHealthFormatter.Checking();
        SetStatusMessage(checking.StatusMessage, StatusSeverity.Pending);

        ModApiHealthProbeResult result;
        try
        {
            _requestUiRefresh();
            result = await _serverHealthProbe.ProbeAsync(_session.Token);
        }
        catch (OperationCanceledException ex)
        {
            var cancellation = HistoryPanelCancellationRouter.Resolve(
                _session.IsCurrent(sessionVersion)
            );
            if (cancellation == HistoryPanelCancellationDisposition.AbandonStaleRequest)
            {
                logOperation.Abandon();
                return;
            }

            _state.ServerHealthProbeInProgress = false;
            if (
                logOperation.TryComplete(
                    HistoryPanelServerHealthTerminalStatus.Failed,
                    HistoryPanelServerHealthReasonCode.Canceled,
                    ex,
                    out var terminal
                )
            )
                HistoryPanelLogWriter.EmitServerHealthTerminal(terminal);
            SetStatusMessage(
                HistoryPanelText.ServerHealthFailed(0, ex.Message),
                StatusSeverity.Failure
            );
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
            {
                logOperation.Abandon();
                return;
            }

            _state.ServerHealthProbeInProgress = false;
            SetStatusMessage(
                HistoryPanelText.ServerHealthFailed(0, ex.Message),
                StatusSeverity.Failure
            );
            if (
                logOperation.TryComplete(
                    HistoryPanelServerHealthTerminalStatus.Failed,
                    HistoryPanelServerHealthReasonCode.UnexpectedException,
                    ex,
                    out var terminal
                )
            )
                HistoryPanelLogWriter.EmitServerHealthTerminal(terminal);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
        {
            logOperation.Abandon();
            return;
        }

        _state.ServerHealthProbeInProgress = false;
        if (result.Succeeded)
        {
            if (
                logOperation.TryComplete(
                    HistoryPanelServerHealthTerminalStatus.Succeeded,
                    HistoryPanelServerHealthReasonCode.Completed,
                    exception: null,
                    out var terminal
                )
            )
                HistoryPanelLogWriter.EmitServerHealthTerminal(terminal);
        }
        else
        {
            if (
                logOperation.TryComplete(
                    HistoryPanelServerHealthTerminalStatus.Failed,
                    HistoryPanelServerHealthReasonClassifier.Classify(result.Error),
                    exception: null,
                    out var terminal
                )
            )
                HistoryPanelLogWriter.EmitServerHealthTerminal(terminal);
        }
        var display = HistoryPanelServerHealthFormatter.FromProbeResult(result);
        SetStatusMessage(
            display.StatusMessage,
            result.Succeeded ? StatusSeverity.Success : StatusSeverity.Failure
        );
        _requestUiRefresh();
    }

    public async Task TryRedeemBazaarDbAccountAsync(string? code)
    {
        if (_state.AccountLinkInProgress)
        {
            SetAccountLinkBanner(
                HistoryPanelText.AccountLink.AlreadyRunning(),
                StatusSeverity.Neutral
            );
            _requestUiRefresh();
            return;
        }

        var logRequest = StartAccountLinkLogRequest(AccountLinkMethod.Redeem);
        string? accountId;
        try
        {
            accountId = RefreshAccountLinkIdentityFromGame(clearBanner: false);
        }
        catch (Exception ex)
        {
            logRequest.Failed(AccountLinkReason.UnexpectedException, ex);
            throw;
        }
        var trimmedCode = code?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.SignedOut(), StatusSeverity.Failure);
            logRequest.Skipped(AccountLinkReason.SignedOut);
            _requestUiRefresh();
            return;
        }

        if (string.IsNullOrEmpty(trimmedCode))
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.EmptyCode(), StatusSeverity.Failure);
            logRequest.Skipped(AccountLinkReason.EmptyCode);
            _requestUiRefresh();
            return;
        }

        if (_linkClient == null)
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            logRequest.Skipped(AccountLinkReason.ClientUnavailable);
            _requestUiRefresh();
            return;
        }

        _state.AccountLinkInProgress = true;
        SetAccountLinkBanner(HistoryPanelText.AccountLink.Linking(), StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        BazaarDbLinkResult result;
        try
        {
            result = await _linkClient.RedeemAsync(trimmedCode, accountId, _session.Token);
        }
        catch (OperationCanceledException ex)
        {
            if (!_session.IsCurrent(sessionVersion))
            {
                logRequest.Abandon();
                return; // panel closed / re-opened mid-flight: discard silently.
            }

            // Still the active session, so this is the HttpClient self-timeout, not a user cancel
            // (a real session cancel bumps the version above). Surface it as a transport failure
            // instead of silently clearing the banner.
            _state.AccountLinkInProgress = false;
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            logRequest.Failed(AccountLinkReason.RequestTimeout, ex);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
            {
                logRequest.Abandon();
                return;
            }

            _state.AccountLinkInProgress = false;
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            logRequest.Failed(AccountLinkReason.UnexpectedException, ex);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
        {
            logRequest.Abandon();
            return;
        }

        _state.AccountLinkInProgress = false;
        string? currentAccountId;
        try
        {
            currentAccountId = NormalizeAccountId(BppClientCacheBridge.TryGetProfileAccountId());
        }
        catch (Exception ex)
        {
            logRequest.Failed(AccountLinkReason.UnexpectedException, ex);
            throw;
        }
        if (!string.Equals(currentAccountId, accountId, StringComparison.Ordinal))
        {
            try
            {
                RefreshAccountLinkIdentityFromGame();
            }
            catch (Exception ex)
            {
                logRequest.Failed(AccountLinkReason.UnexpectedException, ex);
                throw;
            }
            logRequest.Skipped(AccountLinkReason.AccountChanged);
            _requestUiRefresh();
            return;
        }

        // Only a confirmed 200 link persists the local hint and collapses to the status row. 409 means the
        // game account is already linked to a DIFFERENT BazaarDB user (contract), and every error
        // outcome must leave the form open with a failure banner. See OutcomeConfirmsLink.
        if (OutcomeConfirmsLink(result.Outcome))
        {
            _state.LocalLinkedHint = true;
            _state.AccountLinkExpanded = false;
            try
            {
                _accountLinkStore.SaveHint(accountId);
            }
            catch (Exception ex)
            {
                logRequest.Failed(AccountLinkReason.UnexpectedException, ex);
                throw;
            }
            logRequest.Succeeded();
        }
        else
            logRequest.Failed(result.Outcome);

        SetAccountLinkBanner(
            RedeemBannerMessage(result.Outcome),
            RedeemBannerSeverity(result.Outcome)
        );
        _requestUiRefresh();
    }

    // Contract rule, isolated for testability: ONLY a successful 200 redeem confirms the link, so it
    // is the only outcome that may persist the local linked hint. 409/AlreadyLinked (a different
    // BazaarDB user) and every error outcome must return false.
    internal static bool OutcomeConfirmsLink(BazaarDbLinkOutcome outcome) =>
        outcome == BazaarDbLinkOutcome.Linked;

    private static StatusSeverity RedeemBannerSeverity(BazaarDbLinkOutcome outcome) =>
        OutcomeConfirmsLink(outcome) ? StatusSeverity.Success : StatusSeverity.Failure;

    private static string RedeemBannerMessage(BazaarDbLinkOutcome outcome) =>
        outcome switch
        {
            BazaarDbLinkOutcome.Linked => HistoryPanelText.AccountLink.Linked(),
            BazaarDbLinkOutcome.AlreadyLinked => HistoryPanelText.AccountLink.AlreadyLinked(),
            BazaarDbLinkOutcome.InvalidOrExpired or BazaarDbLinkOutcome.MissingFields =>
                HistoryPanelText.AccountLink.InvalidOrExpired(),
            BazaarDbLinkOutcome.ServerError => HistoryPanelText.AccountLink.ServerBusy(),
            _ => HistoryPanelText.AccountLink.Offline(),
        };

    public void ToggleAccountLinkForm()
    {
        if (_state.AccountLinkInProgress)
            return;

        if (_state.AccountLinkExpanded)
        {
            _state.AccountLinkExpanded = false;
            SetAccountLinkBanner(null, StatusSeverity.Neutral);
            _requestUiRefresh();
            return;
        }

        var accountId = RefreshAccountLinkIdentityFromGame();
        if (string.IsNullOrWhiteSpace(accountId))
        {
            _requestUiRefresh();
            return;
        }

        _state.AccountLinkExpanded = true;
        _requestUiRefresh();
    }

    public void MarkAccountLinkedManually()
    {
        if (_state.AccountLinkInProgress)
            return;

        var logRequest = StartAccountLinkLogRequest(AccountLinkMethod.Manual);
        string? accountId;
        try
        {
            accountId = RefreshAccountLinkIdentityFromGame(clearBanner: false);
        }
        catch (Exception ex)
        {
            logRequest.Failed(AccountLinkReason.UnexpectedException, ex);
            throw;
        }
        if (string.IsNullOrWhiteSpace(accountId))
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.SignedOut(), StatusSeverity.Failure);
            logRequest.Skipped(AccountLinkReason.SignedOut);
            _requestUiRefresh();
            return;
        }

        _state.LocalLinkedHint = true;
        _state.AccountLinkExpanded = false;
        try
        {
            _accountLinkStore.SaveHint(accountId);
        }
        catch (Exception ex)
        {
            logRequest.Failed(AccountLinkReason.UnexpectedException, ex);
            throw;
        }
        SetAccountLinkBanner(null, StatusSeverity.Neutral);
        logRequest.Succeeded();
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

        var logOperation = new HistoryPanelGhostSyncLogOperation(Guid.NewGuid().ToString("N"));

        _state.GhostSyncInProgress = true;
        var sessionVersion = _session.Version;
        SetStatusMessage(HistoryPanelText.SyncingGhostBattles(), StatusSeverity.Pending);

        HistoryPanelAttemptResult syncResult;
        try
        {
            _requestUiRefresh();
            syncResult = await _dataService.SyncGhostBattlesAsync(_session.Token);
        }
        catch (OperationCanceledException ex)
        {
            var cancellation = HistoryPanelCancellationRouter.Resolve(
                _session.IsCurrent(sessionVersion)
            );
            if (cancellation == HistoryPanelCancellationDisposition.AbandonStaleRequest)
            {
                logOperation.Abandon();
                return;
            }

            _state.GhostSyncInProgress = false;
            if (
                logOperation.TryFail(HistoryPanelGhostSyncReasonCode.Canceled, ex, out var terminal)
            )
                HistoryPanelLogWriter.EmitGhostSyncTerminal(terminal);
            SetStatusMessage(HistoryPanelText.GhostSyncFailed(ex.Message), StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
            {
                logOperation.Abandon();
                return;
            }

            _state.GhostSyncInProgress = false;
            SetStatusMessage(HistoryPanelText.GhostSyncFailed(ex.Message), StatusSeverity.Failure);
            if (
                logOperation.TryFail(
                    HistoryPanelGhostSyncReasonCode.UnexpectedException,
                    ex,
                    out var terminal
                )
            )
                HistoryPanelLogWriter.EmitGhostSyncTerminal(terminal);
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
        {
            logOperation.Abandon();
            return;
        }

        _state.GhostSyncInProgress = false;
        if (!syncResult.Succeeded)
        {
            if (logOperation.TryFail(syncResult.ReasonCode, syncResult.Error, out var terminal))
                HistoryPanelLogWriter.EmitGhostSyncTerminal(terminal);
            SetStatusMessage(syncResult.StatusMessage, StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }

        if (logOperation.TrySucceed(syncResult.ImportedCount, out var succeeded))
            HistoryPanelLogWriter.EmitGhostSyncTerminal(succeeded);
        SetStatusMessage(syncResult.StatusMessage, StatusSeverity.Success);

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
            if (
                HistoryPanelGhostBattleFilter.Matches(
                    _state.GhostBattleFilter,
                    _state.GhostDayMin10,
                    battle
                )
            )
                _state.FilteredGhostBattles.Add(battle);
        }

        _state.FilteredGhostBattlesDirty = false;
        return _state.FilteredGhostBattles;
    }

    public IReadOnlyList<HistoryRunRecord> GetFilteredRuns()
    {
        if (!_state.FilteredRunsDirty)
            return _state.FilteredRuns;

        _state.FilteredRuns.Clear();
        foreach (var run in _state.Runs)
        {
            if (HistoryPanelRunHeroFilter.Matches(_state.SelectedRunHero, run))
                _state.FilteredRuns.Add(run);
        }

        _state.FilteredRunsDirty = false;
        return _state.FilteredRuns;
    }

    public bool IsDeleteRunConfirmationActive(string runId, float now)
    {
        return _state.DeleteRunConfirmation.IsActiveFor(runId, now);
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
            BppLog.ErrorEvent(
                HistoryPanelLogEvents.DataLoadFailed,
                error,
                HistoryPanelLogEvents.DataDataset.Bind(HistoryPanelDataset.SelectedRunBattles),
                HistoryPanelLogEvents.DataRunId.Bind(run.RunId)
            );
        }
    }

    private HistoryRunRecord? GetSelectedRun()
    {
        var filteredRuns = GetFilteredRuns();
        if (filteredRuns.Count == 0)
            return null;

        _state.SelectedRunIndex = ClampIndex(_state.SelectedRunIndex, filteredRuns.Count);
        return _state.GetSelectedRun(filteredRuns);
    }

    private static int ClampIndex(int index, int count)
    {
        if (count <= 0)
            return 0;

        if (index < 0)
            return 0;

        return index >= count ? count - 1 : index;
    }

    private void ClearDeleteRunConfirmation()
    {
        _state.DeleteRunConfirmation = default;
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

    private string? RefreshAccountLinkIdentityFromGame(bool clearBanner = true)
    {
        var accountId = NormalizeAccountId(BppClientCacheBridge.TryGetProfileAccountId());

        _state.CachedAccountId = accountId;
        if (clearBanner)
            SetAccountLinkBanner(null, StatusSeverity.Neutral);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            _state.LocalLinkedHint = false;
            _state.AccountLinkExpanded = false;
            return null;
        }

        _state.LocalLinkedHint = _accountLinkStore.IsLinked(accountId);
        return accountId;
    }

    private void SetAccountLinkBanner(string? message, StatusSeverity severity)
    {
        _state.AccountLinkBannerMessage = message;
        _state.AccountLinkBannerSeverity = string.IsNullOrWhiteSpace(message)
            ? StatusSeverity.Neutral
            : severity;
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

    private static string? NormalizeAccountId(string? accountId)
    {
        var normalized = accountId?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static AccountLinkLogRequest StartAccountLinkLogRequest(AccountLinkMethod method) =>
        new(Guid.NewGuid().ToString("N"), method, HistoryPanelAccountLinkBppLogSink.Instance);

    private void InvalidateFilteredGhostBattles()
    {
        _state.FilteredGhostBattlesDirty = true;
    }

    private void InvalidateFilteredRuns()
    {
        _state.FilteredRunsDirty = true;
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
