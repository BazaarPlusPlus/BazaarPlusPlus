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
        if (string.IsNullOrWhiteSpace(accountId))
        {
            _requestUiRefresh();
            return;
        }

        _state.BppAccountPhase = BppAccountFlowPhase.EnterBindCode;
        _requestUiRefresh();
    }

    public async Task TrySubmitBppAccountInputAsync(string? input)
    {
        if (_state.BppAccountPhase == BppAccountFlowPhase.EnterBindCode)
            await TryBindBppAccountAsync(input);
    }

    public async Task TryBindBppAccountAsync(string? bindCode)
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
                BppAccountAction.Bind,
                BppAccountLogReason.SignedOut
            );
            _requestUiRefresh();
            return;
        }

        var trimmedCode = bindCode?.Trim() ?? string.Empty;
        if (trimmedCode.Length == 0)
        {
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.EmptyBindCode(),
                StatusSeverity.Failure
            );
            BppAccountLog.Skipped(
                logRequestId,
                BppAccountAction.Bind,
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
                BppAccountAction.Bind,
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
                BppAccountAction.Bind,
                BppAccountLogReason.GameAuthUnavailable
            );
            _requestUiRefresh();
            return;
        }

        _state.BppAccountInProgress = true;
        SetBppAccountBanner(HistoryPanelText.BppAccount.Working(), StatusSeverity.Pending);
        _requestUiRefresh();

        var sessionVersion = _session.Version;
        BppAccountBindResult result;
        try
        {
            result = await _modApiSession.BindBppAccountAsync(
                trimmedCode,
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
                BppAccountAction.Bind,
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
                BppAccountAction.Bind,
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
                BppAccountAction.Bind,
                BppAccountLogReason.AccountChanged
            );
            _requestUiRefresh();
            return;
        }

        if (result.Succeeded)
        {
            ClearPendingBppAccountFlow();
            SetBppAccountBanner(
                HistoryPanelText.BppAccount.BindSucceeded(result.EmailMasked!, result.UserName!),
                StatusSeverity.Success
            );
            BppAccountLog.Succeeded(logRequestId, BppAccountAction.Bind);
            _requestUiRefresh();
            return;
        }

        BppAccountLog.Failed(
            logRequestId,
            BppAccountAction.Bind,
            BppAccountLog.MapServerError(result.ErrorCode)
        );
        SetBppAccountBanner(
            HistoryPanelText.BppAccount.MapError(result.ErrorCode),
            StatusSeverity.Failure
        );
        _requestUiRefresh();
    }

    private string? RefreshBppAccountIdentityFromGame(bool clearBanner)
    {
        ObserveAccount();
        if (clearBanner)
            SetBppAccountBanner(null, StatusSeverity.Neutral);
        return _state.CachedAccountId;
    }

    private void ClearPendingBppAccountFlow()
    {
        _state.BppAccountPhase = BppAccountFlowPhase.Idle;
        _state.BppAccountInProgress = false;
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
}
