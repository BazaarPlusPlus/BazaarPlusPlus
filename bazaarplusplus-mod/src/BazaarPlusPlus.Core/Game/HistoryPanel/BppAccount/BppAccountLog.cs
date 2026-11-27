#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.HistoryPanel.BppAccount;

public enum BppAccountLogReason
{
    SignedOut,
    EmptyInput,
    GameAuthUnavailable,
    ClientUnavailable,
    AccountChanged,
    AccountMismatch,
    GameTokenRejected,
    GameApiUnavailable,
    GameAccountAlreadyBound,
    InvalidBindCode,
    RateLimited,
    RequestTimeout,
    ServerError,
    Transport,
    UnexpectedException,
}

/// <summary>Keep game tokens, bind codes, emails, account IDs, and response bodies out of logs.</summary>
public static class BppAccountLog
{
    private const string BindAction = "Bind";

    internal static void Succeeded(string requestId) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.bpp_account.succeeded"),
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("action", BindAction)
        );

    internal static void Failed(string requestId, BppAccountLogReason reason) =>
        BppLog.ErrorEvent(
            new BppLogEvent(
                BppLogFeatureScope.HistoryPanel,
                "history_panel.bpp_account.failed",
                storm: []
            ),
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("action", BindAction),
            ("reason_code", reason)
        );

    internal static void Skipped(string requestId, BppAccountLogReason reason) =>
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.bpp_account.skipped"),
            () =>
                [
                    ("request_id", requestId, BppLogCorrelationPolicy.Short),
                    ("action", BindAction),
                    ("reason_code", reason),
                ]
        );

    public static BppAccountLogReason MapServerError(string? code) =>
        code switch
        {
            "account_mismatch" => BppAccountLogReason.AccountMismatch,
            "game_token_rejected" => BppAccountLogReason.GameTokenRejected,
            "game_api_unavailable" => BppAccountLogReason.GameApiUnavailable,
            "game_account_already_bound" => BppAccountLogReason.GameAccountAlreadyBound,
            "invalid_bind_code" => BppAccountLogReason.InvalidBindCode,
            "rate_limited" => BppAccountLogReason.RateLimited,
            "request_timeout" => BppAccountLogReason.RequestTimeout,
            "transport_error" => BppAccountLogReason.Transport,
            _ => BppAccountLogReason.ServerError,
        };
}
