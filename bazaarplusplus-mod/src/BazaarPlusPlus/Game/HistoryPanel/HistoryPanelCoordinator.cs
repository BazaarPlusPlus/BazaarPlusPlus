#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.AccountLink;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Storage;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.ModApi.Clients;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed partial class HistoryPanelCoordinator : IDisposable
{
    private readonly HistoryPanelState _state;
    private readonly IRunContext _runContext;
    private readonly HistoryPanelDataService _dataService;
    private readonly HistoryPanelReplayService _replayService;
    private readonly ModApiSession? _modApiSession;
    private readonly BazaarDbLinkClient? _linkClient;
    private readonly BazaarDbAccountLinkStore _accountLinkStore;
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
        _runContext = dependencies.RunContext;
        _dataService = dependencies.DataService;
        _replayService = dependencies.ReplayService;
        _modApiSession = dependencies.ModApiSession;
        _linkClient = dependencies.AccountLinkClient;
        _accountLinkStore = dependencies.AccountLinkStore;
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
        _archiveReads.Clear();
        _battleReads.Clear();
        _detailReads.Clear();
        _maintenanceReads.Clear();
        _session.Dispose();
    }

    public void OnPanelShown(bool resumeSelection = false)
    {
        _session.Begin();
        _state.AccountLinkExpanded = false;
        // Begin() cancels any in-flight redeem, whose continuations bail on !IsCurrent without
        // resetting state. OnPanelHidden has already cleared the flag (the Overlay Panel Host
        // answers a second open with AlreadyInState); resetting here too keeps the toggle guard
        // from leaving the account-link row permanently inert.
        _state.AccountLinkInProgress = false;
        // Adopt the account inside this session; routing through ObserveAccount would begin a
        // second session and sync twice.
        AdoptProfileAccount();
        SetAccountLinkBanner(null, StatusSeverity.Neutral);
        RefreshAccountLinkHint();
        _state.ReplayActionInProgress = false;
        _state.ReplayActionBattleId = null;
        _state.ReplayFailureMessage = null;
        if (resumeSelection)
        {
            ClearTransientStatus();
            LoadArchive(
                new(
                    _state.SectionMode == HistorySectionMode.Ghost
                        ? _state.GhostPage.First
                        : _state.RunPage.First,
                    Inclusive: true
                ),
                true
            );
        }
        else
            RefreshSectionOnEntry();
        StartGhostMaintenance();
    }

    public void OnPanelHidden()
    {
        _state.GhostSync = GhostSyncPhase.NotStarted;
        _state.ReplayActionInProgress = false;
        _state.ServerHealthProbeInProgress = false;
        _state.AccountLinkInProgress = false;
        ClearDeleteRunConfirmation();
        _archiveReads.Clear();
        _battleReads.Clear();
        _detailReads.Clear();
        _maintenanceReads.Clear();
        _session.End();
    }

    public void Tick(float now)
    {
        ObserveAccount();
        if (!_state.DeleteRunConfirmation.HasExpired(now))
            return;

        var shouldClearStatus = _state.ShouldClearStatusWhenDeleteConfirmationExpires();
        ClearDeleteRunConfirmation();
        if (shouldClearStatus)
            SetStatusMessage(null);
        _requestUiRefresh();
    }

    private void RefreshSectionOnEntry()
    {
        RefreshData();

        if (_state.SectionMode == HistorySectionMode.Ghost && _dataService.CanSyncGhostBattles)
            _ = TrySyncGhostBattlesAsync();
    }

    private void RefreshData() => LoadArchive(new(), preserveSelection: true);

    private void RefreshGhostData() =>
        LoadArchive(new(_state.GhostPage.First, Inclusive: true), preserveSelection: true);

    public void SetSectionMode(HistorySectionMode mode)
    {
        if (_state.SectionMode == mode)
            return;
        _state.SectionMode = mode;
        ClearDetail();
        RefreshSectionOnEntry();
    }

    public void SetGhostBattleFilter(GhostBattleFilter filter)
    {
        if (_state.GhostBattleFilter == filter)
            return;
        _state.GhostBattleFilter = filter;
        LoadArchive(new(AnchorId: ActiveBattle()?.BattleId), preserveSelection: true);
    }

    public void SetRunHeroFilter(string hero)
    {
        var canonical = HistoryPanelHeroPresentation.CanonicalFilterId(hero);
        _state.SelectedRunHero = HistoryPanelHeroPresentation.IsSelected(
            _state.SelectedRunHero,
            hero
        )
            ? null
            : canonical;
        LoadArchive(new(AnchorId: GetSelectedRun()?.RunId), preserveSelection: true);
    }

    public void ToggleGhostDayMin10() => SetGhostDayMin10(!_state.GhostDayMin10);

    public void SetGhostDayMin10(bool value)
    {
        if (_state.GhostDayMin10 == value)
            return;
        _state.GhostDayMin10 = value;
        LoadArchive(new(AnchorId: ActiveBattle()?.BattleId), preserveSelection: true);
    }

    public void SelectRun(int index)
    {
        if (index < 0 || index >= _state.Runs.Count || index == _state.SelectedRunIndex)
            return;
        _state.SelectedRunIndex = index;
        ClearDeleteRunConfirmation();
        LoadBattlesForSelectedRun();
        _requestUiRefresh();
    }

    public void SelectBattle(int index)
    {
        var rows =
            _state.SectionMode == HistorySectionMode.Ghost ? _state.GhostBattles : _state.Battles;
        if (index < 0 || index >= rows.Count)
            return;
        if (_state.SectionMode == HistorySectionMode.Ghost)
            _state.SelectedGhostBattleIndex = index;
        else
            _state.SelectedBattleIndex = index;
        LoadSelectedDetail();
        _requestUiRefresh();
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
            _runContext.IsInGameRun,
            _runContext.CurrentServerRunId,
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

        var logRequestId = NewLogRequestId();

        // Recording must be feasible before a record-and-replay request proceeds; otherwise we
        // surface the reason and refuse rather than silently starting a no-video replay.
        if (recordVideo)
        {
            var canRecord = _replayService.CanRecordReplay(
                battle,
                out var recordUnavailableReason,
                out var recordingReasonCode
            );
            LogReplayPreflight(
                logRequestId,
                battle.BattleId,
                recordVideo,
                canRecord,
                recordingReasonCode
            );
            if (!canRecord)
            {
                SetStatusMessage(recordUnavailableReason);
                LogReplayFailed(
                    logRequestId,
                    battle.BattleId,
                    recordVideo,
                    recordingReasonCode,
                    exception: null
                );
                _requestUiRefresh();
                return;
            }
        }

        _state.ReplayActionInProgress = true;
        _state.ReplayActionBattleId = battle.BattleId;
        _state.ReplayFailureMessage = null;
        var sessionVersion = _session.Version;
        var replayAccount = _state.CachedAccountId;
        SetStatusMessage(
            battle.Source == HistoryBattleSource.Ghost && battle.Replay != ReplayAvailability.Saved
                ? HistoryPanelText.DownloadingGhostReplay()
                : HistoryPanelText.StartingReplay(),
            StatusSeverity.Pending
        );
        HistoryPanelReplayAttemptResult replayResult;
        try
        {
            _requestPreviewRefresh();
            _requestUiRefresh();
            replayResult = await _replayService.ReplayBattleAsync(
                battle,
                recordVideo,
                _session.Token,
                () =>
                    _session.IsCurrent(sessionVersion)
                    && ActiveBattle()?.BattleId == battle.BattleId
                    && replayAccount
                        == NormalizeAccountId(BppClientCacheBridge.TryGetProfileAccountId())
            );
        }
        catch (OperationCanceledException ex)
        {
            var cancellation = HistoryPanelCancellationRouter.Resolve(
                _session.IsCurrent(sessionVersion)
            );
            if (cancellation == HistoryPanelCancellationDisposition.AbandonStaleRequest)
            {
                return;
            }

            _state.ReplayActionInProgress = false;
            LogReplayFailed(
                logRequestId,
                battle.BattleId,
                recordVideo,
                HistoryPanelReplayReasonCode.Canceled,
                ex
            );
            SetReplayFailure(battle, HistoryPanelText.ReplayFailed(ex.Message));
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
            {
                return;
            }

            _state.ReplayActionInProgress = false;
            SetReplayFailure(battle, HistoryPanelText.ReplayFailed(ex.Message));
            LogReplayFailed(
                logRequestId,
                battle.BattleId,
                recordVideo,
                HistoryPanelReplayReasonCode.UnexpectedException,
                ex
            );
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
        {
            return;
        }

        _state.ReplayActionInProgress = false;
        if (!replayResult.Succeeded)
        {
            LogReplayFailed(
                logRequestId,
                battle.BattleId,
                recordVideo,
                replayResult.ReasonCode,
                replayResult.Exception
            );
            SetReplayFailure(battle, replayResult.StatusMessage);
            _requestUiRefresh();
            return;
        }

        LogReplayAccepted(logRequestId, battle.BattleId, recordVideo);
        SetStatusMessage(replayResult.StatusMessage, StatusSeverity.Success);
        _requestVisibilityChange(false);
    }

    private void SetReplayFailure(HistoryBattleRecord battle, string message)
    {
        _state.ReplayActionBattleId = battle.BattleId;
        _state.ReplayFailureMessage = message;
        SetStatusMessage(message, StatusSeverity.Failure);
        if (
            battle.Source == HistoryBattleSource.Ghost
            && _state.SectionMode == HistorySectionMode.Ghost
        )
            RefreshGhostData();
        _requestPreviewRefresh();
    }

    public bool IsDeleteRunConfirmationActive(string runId, float now) =>
        _state.DeleteRunConfirmation.IsActiveFor(runId, now);

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

        var logRequestId = NewLogRequestId();

        if (!_dataService.TryDeleteRun(run.RunId, out var battleIds, out var error))
        {
            SetStatusMessage(
                HistoryPanelText.RunDeleteFailed(error?.Message ?? HistoryPanelText.Unknown()),
                StatusSeverity.Failure
            );
            LogRunDeleteFailed(
                logRequestId,
                run.RunId,
                battleIds.Count,
                error ?? new InvalidOperationException("Unknown run delete failure.")
            );
            _requestUiRefresh();
            return;
        }

        var cleanupResult = _replayService.CleanupReplayPayloads(battleIds);
        LogRunDeleteCompleted(
            logRequestId,
            run.RunId,
            battleIds.Count,
            cleanupResult.FailedBattleCount,
            cleanupResult.Exception
        );
        var deletedMessage = HistoryPanelText.DeletedRun(
            HistoryPanelFormatter.ShortenRunId(run.RunId),
            battleIds.Count
        );
        LoadArchive(new(_state.RunPage.First, Inclusive: true), preserveSelection: true);
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

        if (_modApiSession == null)
        {
            var unavailable = HistoryPanelServerHealthFormatter.Unavailable();
            SetStatusMessage(unavailable.StatusMessage);
            _requestUiRefresh();
            return;
        }

        var logRequestId = NewLogRequestId();
        var logStartedAt = LogTimestampMilliseconds();

        _state.ServerHealthProbeInProgress = true;
        var sessionVersion = _session.Version;
        var checking = HistoryPanelServerHealthFormatter.Checking();
        SetStatusMessage(checking.StatusMessage, StatusSeverity.Pending);

        ModApiHealthProbeResult result;
        try
        {
            _requestUiRefresh();
            result = await _modApiSession.ProbeHealthAsync(_session.Token);
        }
        catch (OperationCanceledException ex)
        {
            var cancellation = HistoryPanelCancellationRouter.Resolve(
                _session.IsCurrent(sessionVersion)
            );
            if (cancellation == HistoryPanelCancellationDisposition.AbandonStaleRequest)
            {
                return;
            }

            _state.ServerHealthProbeInProgress = false;
            LogServerHealth(
                logRequestId,
                logStartedAt,
                succeeded: false,
                HistoryPanelServerHealthReasonCode.Canceled,
                ex
            );
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
                return;
            }

            _state.ServerHealthProbeInProgress = false;
            SetStatusMessage(
                HistoryPanelText.ServerHealthFailed(0, ex.Message),
                StatusSeverity.Failure
            );
            LogServerHealth(
                logRequestId,
                logStartedAt,
                succeeded: false,
                HistoryPanelServerHealthReasonCode.UnexpectedException,
                ex
            );
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
        {
            return;
        }

        _state.ServerHealthProbeInProgress = false;
        if (result.Succeeded)
        {
            LogServerHealth(
                logRequestId,
                logStartedAt,
                succeeded: true,
                HistoryPanelServerHealthReasonCode.Completed,
                exception: null
            );
        }
        else
        {
            LogServerHealth(
                logRequestId,
                logStartedAt,
                succeeded: false,
                HistoryPanelServerHealthReasonClassifier.Classify(result.Error),
                result.DiagnosticException
            );
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

        var logRequestId = NewLogRequestId();
        const AccountLinkMethod linkMethod = AccountLinkMethod.Redeem;
        string? accountId;
        try
        {
            accountId = RefreshAccountLinkIdentityFromGame(clearBanner: false);
        }
        catch (Exception ex)
        {
            AccountLinkLog.Failed(
                logRequestId,
                linkMethod,
                AccountLinkReason.UnexpectedException,
                ex
            );
            throw;
        }
        var trimmedCode = code?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.SignedOut(), StatusSeverity.Failure);
            AccountLinkLog.Skipped(logRequestId, AccountLinkReason.SignedOut);
            _requestUiRefresh();
            return;
        }

        if (string.IsNullOrEmpty(trimmedCode))
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.EmptyCode(), StatusSeverity.Failure);
            AccountLinkLog.Skipped(logRequestId, AccountLinkReason.EmptyCode);
            _requestUiRefresh();
            return;
        }

        if (_linkClient == null)
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            AccountLinkLog.Skipped(logRequestId, AccountLinkReason.ClientUnavailable);
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
                return; // panel closed / re-opened mid-flight: discard silently.
            }

            // Still the active session, so this is the HttpClient self-timeout, not a user cancel
            // (a real session cancel bumps the version above). Surface it as a transport failure
            // instead of silently clearing the banner.
            _state.AccountLinkInProgress = false;
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            AccountLinkLog.Failed(logRequestId, linkMethod, AccountLinkReason.RequestTimeout, ex);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
            {
                return;
            }

            _state.AccountLinkInProgress = false;
            SetAccountLinkBanner(HistoryPanelText.AccountLink.Offline(), StatusSeverity.Failure);
            AccountLinkLog.Failed(
                logRequestId,
                linkMethod,
                AccountLinkReason.UnexpectedException,
                ex
            );
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
        {
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
            AccountLinkLog.Failed(
                logRequestId,
                linkMethod,
                AccountLinkReason.UnexpectedException,
                ex
            );
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
                AccountLinkLog.Failed(
                    logRequestId,
                    linkMethod,
                    AccountLinkReason.UnexpectedException,
                    ex
                );
                throw;
            }
            AccountLinkLog.Skipped(logRequestId, AccountLinkReason.AccountChanged);
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
                AccountLinkLog.Failed(
                    logRequestId,
                    linkMethod,
                    AccountLinkReason.UnexpectedException,
                    ex
                );
                throw;
            }
            AccountLinkLog.Succeeded(logRequestId, linkMethod);
        }
        else
            AccountLinkLog.Failed(
                logRequestId,
                linkMethod,
                result.Outcome,
                result.DiagnosticException
            );

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

        var logRequestId = NewLogRequestId();
        const AccountLinkMethod linkMethod = AccountLinkMethod.Manual;
        string? accountId;
        try
        {
            accountId = RefreshAccountLinkIdentityFromGame(clearBanner: false);
        }
        catch (Exception ex)
        {
            AccountLinkLog.Failed(
                logRequestId,
                linkMethod,
                AccountLinkReason.UnexpectedException,
                ex
            );
            throw;
        }
        if (string.IsNullOrWhiteSpace(accountId))
        {
            SetAccountLinkBanner(HistoryPanelText.AccountLink.SignedOut(), StatusSeverity.Failure);
            AccountLinkLog.Skipped(logRequestId, AccountLinkReason.SignedOut);
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
            AccountLinkLog.Failed(
                logRequestId,
                linkMethod,
                AccountLinkReason.UnexpectedException,
                ex
            );
            throw;
        }
        SetAccountLinkBanner(null, StatusSeverity.Neutral);
        AccountLinkLog.Succeeded(logRequestId, linkMethod);
        _requestUiRefresh();
    }

    public async Task TrySyncGhostBattlesAsync()
    {
        if (_state.GhostSync == GhostSyncPhase.Running)
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

        var logRequestId = NewLogRequestId();

        _state.GhostSync = GhostSyncPhase.Running;
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
                return;
            }

            _state.GhostSync = GhostSyncPhase.Failed;
            LogGhostSync(
                logRequestId,
                succeeded: false,
                0,
                HistoryPanelGhostSyncReasonCode.Canceled,
                ex
            );
            SetStatusMessage(HistoryPanelText.GhostSyncFailed(ex.Message), StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }
        catch (Exception ex)
        {
            if (!_session.IsCurrent(sessionVersion))
            {
                return;
            }

            _state.GhostSync = GhostSyncPhase.Failed;
            SetStatusMessage(HistoryPanelText.GhostSyncFailed(ex.Message), StatusSeverity.Failure);
            LogGhostSync(
                logRequestId,
                succeeded: false,
                0,
                HistoryPanelGhostSyncReasonCode.UnexpectedException,
                ex
            );
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
        {
            return;
        }

        if (!syncResult.Succeeded)
        {
            _state.GhostSync = GhostSyncPhase.Failed;
            LogGhostSync(
                logRequestId,
                succeeded: false,
                0,
                syncResult.ReasonCode,
                syncResult.Error
            );
            SetStatusMessage(syncResult.StatusMessage, StatusSeverity.Failure);
            _requestUiRefresh();
            return;
        }

        _state.GhostSync = GhostSyncPhase.Completed;
        LogGhostSync(
            logRequestId,
            succeeded: true,
            syncResult.ImportedCount,
            HistoryPanelGhostSyncReasonCode.Completed,
            exception: null
        );
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

    public IReadOnlyList<HistoryBattleRecord> GetFilteredGhostBattles() => _state.GhostBattles;

    public IReadOnlyList<HistoryRunRecord> GetFilteredRuns() => _state.Runs;

    private HistoryRunRecord? GetSelectedRun() => _state.GetSelectedRun(_state.Runs);

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
            && _state.GhostSync != GhostSyncPhase.Running
            && !_state.ServerHealthProbeInProgress
        )
            SetStatusMessage(null);
    }

    // A changed account goes through ObserveAccount first, so the Ghost list and sync follow it
    // even when the account-link row is the first to see the new profile.
    private string? RefreshAccountLinkIdentityFromGame(bool clearBanner = true)
    {
        ObserveAccount();
        if (clearBanner)
            SetAccountLinkBanner(null, StatusSeverity.Neutral);
        RefreshAccountLinkHint();
        return _state.CachedAccountId;
    }

    private void RefreshAccountLinkHint()
    {
        var accountId = _state.CachedAccountId;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            _state.LocalLinkedHint = false;
            _state.AccountLinkExpanded = false;
            return;
        }

        _state.LocalLinkedHint = _accountLinkStore.IsLinked(accountId);
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
}
