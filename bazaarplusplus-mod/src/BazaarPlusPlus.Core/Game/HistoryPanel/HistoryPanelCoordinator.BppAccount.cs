#nullable enable
using BazaarPlusPlus.Core.GameAuth;
using BazaarPlusPlus.Game.HistoryPanel.BppAccount;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel;

public sealed partial class HistoryPanelCoordinator
{
    public void ToggleBppAccountForm()
    {
        if (_state.BppAccountInProgress)
            return;

        if (_state.BppAccountPhase != BppAccountFlowPhase.Idle)
        {
            ClearPendingBppAccountFlow();
            SetBppAccountBanner(null, StatusSeverity.Neutral);
            _requestUiRefresh();
            return;
        }

        var accountId = RefreshBppAccountIdentityFromGame(clearBanner: true);
        if (string.IsNullOrWhiteSpace(accountId) || _state.BppAccountBound)
        {
            _requestUiRefresh();
            return;
        }

        _state.BppAccountPhase = BppAccountFlowPhase.EnterEmail;
        _requestUiRefresh();
    }

    public void OpenBppAccountActivateDevice()
    {
        if (_state.BppAccountInProgress)
            return;

        var accountId = RefreshBppAccountIdentityFromGame(clearBanner: true);
        if (string.IsNullOrWhiteSpace(accountId) || _state.BppAccountBound)
        {
            _requestUiRefresh();
            return;
        }

        _state.BppAccountPendingId = null;
        _state.BppAccountPendingUserName = null;
        _state.BppAccountPendingEmailMasked = null;
        _state.BppAccountPhase = BppAccountFlowPhase.ConfirmActivateDevice;
        _requestUiRefresh();
    }

    public async Task TrySubmitBppAccountInputAsync(string? input)
    {
        switch (_state.BppAccountPhase)
        {
            case BppAccountFlowPhase.EnterEmail:
                await TryStartBppAccountBindAsync(input);
                break;
            case BppAccountFlowPhase.EnterCode:
                await TryConfirmBppAccountBindAsync(input);
                break;
            case BppAccountFlowPhase.ConfirmActivateDevice:
                await TryActivateBppAccountDeviceAsync();
                break;
        }
    }

    public async Task TryStartBppAccountBindAsync(string? email)
    {
        if (_state.BppAccountInProgress)
            return;

        var logRequestId = NewLogRequestId();
        var accountId = RefreshBppAccountIdentityFromGame(clearBanner: false);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            SetBppAccountBanner(HistoryPanelText.BppAccount.SignedOut(), StatusSeverity.Failure);
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.BindStart,
                BppAccountLogReason.SignedOut
            );
            _requestUiRefresh();
            return;
        }

        var trimmedEmail = email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedEmail) || !trimmedEmail.Contains('@'))
        {
            SetBppAccountBanner(HistoryPanelText.BppAccount.EmptyEmail(), StatusSeverity.Failure);
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.BindStart,
                BppAccountLogReason.EmptyInput
            );
            _requestUiRefresh();
            return;
        }

        if (_modApiSession == null)
        {
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.MapError("transport_error"),
                StatusSeverity.Failure
            );
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.BindStart,
                BppAccountLogReason.ClientUnavailable
            );
            _requestUiRefresh();
            return;
        }

        // Read the game token, platform, and flavor on the host main thread before the first await.
        var auth = TryReadGameAuthSnapshot();
        if (auth == null)
        {
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.GameAuthUnavailable(),
                StatusSeverity.Failure
            );
            BppAccountLog.Failed(
                logRequestId,
                BppAccountAction.BindStart,
                BppAccountLogReason.GameAuthUnavailable
            );
            _requestUiRefresh();
            return;
        }

        _state.BppAccountInProgress = true;
        SetBppAccountBanner(HistoryPanelText.BppAccount.Working(), StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        BppAccountBindStartResult result;
        try
        {
            result = await _modApiSession.StartBppAccountBindAsync(
                trimmedEmail,
                auth.AccessToken,
                accountId!,
                auth.Platform,
                auth.Flavor,
                _session.Token
            );
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;
            _state.BppAccountInProgress = false;
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.MapError("request_timeout"),
                StatusSeverity.Failure
            );
            BppAccountLog.Failed(
                logRequestId,
                BppAccountAction.BindStart,
                BppAccountLogReason.RequestTimeout
            );
            _requestUiRefresh();
            return;
        }
        catch
        {
            if (!_session.IsCurrent(sessionVersion))
                return;
            _state.BppAccountInProgress = false;
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.MapError("transport_error"),
                StatusSeverity.Failure
            );
            BppAccountLog.Failed(
                logRequestId,
                BppAccountAction.BindStart,
                BppAccountLogReason.UnexpectedException
            );
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.BppAccountInProgress = false;
        if (
            !string.Equals(
                NormalizeAccountId(_profileAccountId()),
                accountId,
                StringComparison.Ordinal
            )
        )
        {
            ObserveAccount();
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.BindStart,
                BppAccountLogReason.AccountChanged
            );
            _requestUiRefresh();
            return;
        }

        if (result.Succeeded)
        {
            _state.BppAccountPendingId = result.PendingId;
            _state.BppAccountPendingUserName = result.UserName;
            _state.BppAccountPendingEmailMasked = result.EmailMasked;
            _state.BppAccountPhase = BppAccountFlowPhase.EnterCode;
            SetBppAccountBanner(null, StatusSeverity.Neutral);
            BppAccountLog.Succeeded(logRequestId, BppAccountAction.BindStart);
            _requestUiRefresh();
            return;
        }

        if (string.Equals(result.ErrorCode, "game_account_already_bound", StringComparison.Ordinal))
        {
            _state.BppAccountPendingId = null;
            _state.BppAccountPendingUserName = null;
            _state.BppAccountPendingEmailMasked = result.ConflictEmailMasked;
            _state.BppAccountPhase = BppAccountFlowPhase.ConfirmActivateDevice;
            SetBppAccountBanner(null, StatusSeverity.Neutral);
            BppAccountLog.Failed(
                logRequestId,
                BppAccountAction.BindStart,
                BppAccountLogReason.GameAccountAlreadyBound
            );
            _requestUiRefresh();
            return;
        }

        BppAccountLog.Failed(
            logRequestId,
            BppAccountAction.BindStart,
            BppAccountLog.MapServerError(result.ErrorCode)
        );
        SetBppAccountBanner(
            HistoryPanelText.BppAccount.MapError(result.ErrorCode),
            StatusSeverity.Failure
        );
        _requestUiRefresh();
    }

    public async Task TryConfirmBppAccountBindAsync(string? code)
    {
        if (_state.BppAccountInProgress)
            return;

        var logRequestId = NewLogRequestId();
        var accountId = RefreshBppAccountIdentityFromGame(clearBanner: false);
        var pendingId = _state.BppAccountPendingId;
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(pendingId))
        {
            ClearPendingBppAccountFlow();
            SetBppAccountBanner(HistoryPanelText.BppAccount.SignedOut(), StatusSeverity.Failure);
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.BindConfirm,
                BppAccountLogReason.SignedOut
            );
            _requestUiRefresh();
            return;
        }

        var trimmedCode = code?.Trim() ?? string.Empty;
        if (trimmedCode.Length != 6 || !trimmedCode.All(char.IsDigit))
        {
            SetBppAccountBanner(HistoryPanelText.BppAccount.EmptyCode(), StatusSeverity.Failure);
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.BindConfirm,
                BppAccountLogReason.EmptyInput
            );
            _requestUiRefresh();
            return;
        }

        if (_modApiSession == null)
        {
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.MapError("transport_error"),
                StatusSeverity.Failure
            );
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.BindConfirm,
                BppAccountLogReason.ClientUnavailable
            );
            _requestUiRefresh();
            return;
        }

        _state.BppAccountInProgress = true;
        SetBppAccountBanner(HistoryPanelText.BppAccount.Working(), StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        BppAccountDeviceResult result;
        try
        {
            result = await _modApiSession.ConfirmBppAccountBindAsync(
                pendingId!,
                trimmedCode,
                _session.Token
            );
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;
            _state.BppAccountInProgress = false;
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.MapError("request_timeout"),
                StatusSeverity.Failure
            );
            BppAccountLog.Failed(
                logRequestId,
                BppAccountAction.BindConfirm,
                BppAccountLogReason.RequestTimeout
            );
            _requestUiRefresh();
            return;
        }
        catch
        {
            if (!_session.IsCurrent(sessionVersion))
                return;
            _state.BppAccountInProgress = false;
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.MapError("transport_error"),
                StatusSeverity.Failure
            );
            BppAccountLog.Failed(
                logRequestId,
                BppAccountAction.BindConfirm,
                BppAccountLogReason.UnexpectedException
            );
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.BppAccountInProgress = false;
        if (
            !string.Equals(
                NormalizeAccountId(_profileAccountId()),
                accountId,
                StringComparison.Ordinal
            )
        )
        {
            ObserveAccount();
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.BindConfirm,
                BppAccountLogReason.AccountChanged
            );
            _requestUiRefresh();
            return;
        }

        if (result.Succeeded)
        {
            SaveBoundBppAccountSession(
                accountId!,
                result.DeviceToken!,
                result.UserName!,
                result.EmailMasked!
            );
            ClearPendingBppAccountFlow();
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.BindSucceeded(),
                StatusSeverity.Success
            );
            BppAccountLog.Succeeded(logRequestId, BppAccountAction.BindConfirm);
            _requestUiRefresh();
            return;
        }

        BppAccountLog.Failed(
            logRequestId,
            BppAccountAction.BindConfirm,
            BppAccountLog.MapServerError(result.ErrorCode)
        );
        SetBppAccountBanner(
            HistoryPanelText.BppAccount.MapError(result.ErrorCode),
            StatusSeverity.Failure
        );
        _requestUiRefresh();
    }

    public async Task TryActivateBppAccountDeviceAsync()
    {
        if (_state.BppAccountInProgress)
            return;

        var logRequestId = NewLogRequestId();
        var accountId = RefreshBppAccountIdentityFromGame(clearBanner: false);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            SetBppAccountBanner(HistoryPanelText.BppAccount.SignedOut(), StatusSeverity.Failure);
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.ActivateDevice,
                BppAccountLogReason.SignedOut
            );
            _requestUiRefresh();
            return;
        }

        if (_modApiSession == null)
        {
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.MapError("transport_error"),
                StatusSeverity.Failure
            );
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.ActivateDevice,
                BppAccountLogReason.ClientUnavailable
            );
            _requestUiRefresh();
            return;
        }

        // Read the game token, platform, and flavor on the host main thread before the first await.
        var auth = TryReadGameAuthSnapshot();
        if (auth == null)
        {
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.GameAuthUnavailable(),
                StatusSeverity.Failure
            );
            BppAccountLog.Failed(
                logRequestId,
                BppAccountAction.ActivateDevice,
                BppAccountLogReason.GameAuthUnavailable
            );
            _requestUiRefresh();
            return;
        }

        _state.BppAccountInProgress = true;
        SetBppAccountBanner(HistoryPanelText.BppAccount.Working(), StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        BppAccountDeviceResult result;
        try
        {
            result = await _modApiSession.IssueBppAccountDeviceTokenAsync(
                auth.AccessToken,
                accountId!,
                auth.Platform,
                auth.Flavor,
                _session.Token
            );
        }
        catch (OperationCanceledException)
        {
            if (!_session.IsCurrent(sessionVersion))
                return;
            _state.BppAccountInProgress = false;
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.MapError("request_timeout"),
                StatusSeverity.Failure
            );
            BppAccountLog.Failed(
                logRequestId,
                BppAccountAction.ActivateDevice,
                BppAccountLogReason.RequestTimeout
            );
            _requestUiRefresh();
            return;
        }
        catch
        {
            if (!_session.IsCurrent(sessionVersion))
                return;
            _state.BppAccountInProgress = false;
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.MapError("transport_error"),
                StatusSeverity.Failure
            );
            BppAccountLog.Failed(
                logRequestId,
                BppAccountAction.ActivateDevice,
                BppAccountLogReason.UnexpectedException
            );
            _requestUiRefresh();
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;

        _state.BppAccountInProgress = false;
        if (
            !string.Equals(
                NormalizeAccountId(_profileAccountId()),
                accountId,
                StringComparison.Ordinal
            )
        )
        {
            ObserveAccount();
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.ActivateDevice,
                BppAccountLogReason.AccountChanged
            );
            _requestUiRefresh();
            return;
        }

        if (result.Succeeded)
        {
            SaveBoundBppAccountSession(
                accountId!,
                result.DeviceToken!,
                result.UserName!,
                result.EmailMasked!
            );
            ClearPendingBppAccountFlow();
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.DeviceActivated(),
                StatusSeverity.Success
            );
            BppAccountLog.Succeeded(logRequestId, BppAccountAction.ActivateDevice);
            _requestUiRefresh();
            return;
        }

        BppAccountLog.Failed(
            logRequestId,
            BppAccountAction.ActivateDevice,
            BppAccountLog.MapServerError(result.ErrorCode)
        );
        SetBppAccountBanner(
            HistoryPanelText.BppAccount.MapError(result.ErrorCode),
            StatusSeverity.Failure
        );
        _requestUiRefresh();
    }

    public async Task RefreshBppAccountSessionAsync()
    {
        var accountId = _state.CachedAccountId;
        if (
            string.IsNullOrWhiteSpace(accountId)
            || _modApiSession == null
            || _bppAccountStore == null
        )
            return;

        var stored = _bppAccountStore.TryRead();
        if (
            stored == null
            || !string.Equals(stored.GameAccountId, accountId, StringComparison.Ordinal)
        )
            return;

        var logRequestId = NewLogRequestId();
        var sessionVersion = _session.Version;
        BppAccountMeResult result;
        try
        {
            result = await _modApiSession.GetBppAccountMeAsync(stored.DeviceToken, _session.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (!_session.IsCurrent(sessionVersion))
            return;
        if (!string.Equals(_state.CachedAccountId, accountId, StringComparison.Ordinal))
            return;

        if (result.IsUnauthorized)
        {
            _bppAccountStore.Clear();
            _state.BppAccountBound = false;
            _state.BppAccountEmailMasked = null;
            _state.BppAccountUserName = null;
            BppAccountLog.TokenCleared(logRequestId, BppAccountLogReason.Unauthorized);
            _requestUiRefresh();
            return;
        }

        if (result.Succeeded)
        {
            if (
                !string.Equals(stored.UserName, result.UserName, StringComparison.Ordinal)
                || !string.Equals(stored.EmailMasked, result.EmailMasked, StringComparison.Ordinal)
            )
            {
                _bppAccountStore.Save(
                    new BppStoredAccountSession(
                        stored.GameAccountId,
                        stored.DeviceToken,
                        result.UserName!,
                        result.EmailMasked!
                    )
                );
            }
            _state.BppAccountBound = true;
            _state.BppAccountUserName = result.UserName;
            _state.BppAccountEmailMasked = result.EmailMasked;
            _requestUiRefresh();
        }
    }

    private string? RefreshBppAccountIdentityFromGame(bool clearBanner)
    {
        ObserveAccount();
        if (clearBanner)
            SetBppAccountBanner(null, StatusSeverity.Neutral);
        RefreshBppAccountFromStore();
        return _state.CachedAccountId;
    }

    private void RefreshBppAccountFromStore()
    {
        _state.BppAccountLocalDisplayName = TryReadProfileDisplayName();
        var accountId = _state.CachedAccountId;
        if (string.IsNullOrWhiteSpace(accountId) || _bppAccountStore == null)
        {
            _state.BppAccountBound = false;
            _state.BppAccountEmailMasked = null;
            _state.BppAccountUserName = null;
            return;
        }

        var stored = _bppAccountStore.TryRead();
        if (
            stored != null
            && string.Equals(stored.GameAccountId, accountId, StringComparison.Ordinal)
        )
        {
            _state.BppAccountBound = true;
            _state.BppAccountEmailMasked = stored.EmailMasked;
            _state.BppAccountUserName = stored.UserName;
        }
        else
        {
            _state.BppAccountBound = false;
            _state.BppAccountEmailMasked = null;
            _state.BppAccountUserName = null;
        }
    }

    private void SaveBoundBppAccountSession(
        string gameAccountId,
        string deviceToken,
        string userName,
        string emailMasked
    )
    {
        _bppAccountStore?.Save(
            new BppStoredAccountSession(gameAccountId, deviceToken, userName, emailMasked)
        );
        _state.BppAccountBound = true;
        _state.BppAccountUserName = userName;
        _state.BppAccountEmailMasked = emailMasked;
    }

    private void ClearPendingBppAccountFlow()
    {
        _state.BppAccountPhase = BppAccountFlowPhase.Idle;
        _state.BppAccountInProgress = false;
        _state.BppAccountPendingId = null;
        _state.BppAccountPendingUserName = null;
        _state.BppAccountPendingEmailMasked = null;
    }

    private void SetBppAccountBanner(string? message, StatusSeverity severity)
    {
        _state.BppAccountBannerMessage = message;
        _state.BppAccountBannerSeverity = string.IsNullOrWhiteSpace(message)
            ? StatusSeverity.Neutral
            : severity;
    }

    private BppGameAuthSnapshot? TryReadGameAuthSnapshot()
    {
        try
        {
            var snapshot = _gameAuthReader?.Invoke();
            if (
                snapshot == null
                || string.IsNullOrWhiteSpace(snapshot.AccessToken)
                || string.IsNullOrWhiteSpace(snapshot.Platform)
                || string.IsNullOrWhiteSpace(snapshot.Flavor)
            )
                return null;
            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    private string? TryReadProfileDisplayName()
    {
        try
        {
            var name = _profileDisplayName?.Invoke()?.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }
}
