#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi.Clients;

namespace BazaarPlusPlus.Game.HistoryPanel.AccountLink;

internal enum AccountLinkMethod
{
    Redeem,
    Manual,
}

internal enum AccountLinkReason
{
    SignedOut,
    EmptyCode,
    ClientUnavailable,
    AccountChanged,
    RequestTimeout,
    UnexpectedException,
    InvalidOrExpired,
    AlreadyLinked,
    MissingFields,
    ServerError,
    Transport,
    UnexpectedOutcome,
}

/// <summary>
/// The account-link terminal events. Each link attempt writes one of them on every path that
/// returns or throws, and none when a stale panel session abandons it. Only the request id,
/// method, and reason code are written: the account id, link code, token, and server response
/// body never reach a field.
/// </summary>
internal static class AccountLinkLog
{
    internal static void Succeeded(string requestId, AccountLinkMethod method) =>
        BppLog.InfoEvent(
            new BppLogEvent(
                BppLogFeatureScope.HistoryPanel,
                "history_panel.account_link.succeeded"
            ),
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("method", method)
        );

    internal static void Failed(
        string requestId,
        AccountLinkMethod method,
        BazaarDbLinkOutcome outcome,
        Exception? exception = null
    ) => Failed(requestId, method, MapFailure(outcome), exception);

    internal static void Failed(
        string requestId,
        AccountLinkMethod method,
        AccountLinkReason reason,
        Exception? exception = null
    )
    {
        var logEvent = new BppLogEvent(
            BppLogFeatureScope.HistoryPanel,
            "history_panel.account_link.failed",
            storm: []
        );
        var fields = new BppLogField[]
        {
            ("request_id", requestId, BppLogCorrelationPolicy.Short),
            ("method", method),
            ("reason_code", reason),
        };
        if (exception == null)
            BppLog.ErrorEvent(logEvent, fields);
        else
            BppLog.ErrorEvent(logEvent, exception, fields);
    }

    internal static void Skipped(string requestId, AccountLinkReason reason) =>
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.HistoryPanel, "history_panel.account_link.skipped"),
            () =>
                [("request_id", requestId, BppLogCorrelationPolicy.Short), ("reason_code", reason)]
        );

    private static AccountLinkReason MapFailure(BazaarDbLinkOutcome outcome) =>
        outcome switch
        {
            BazaarDbLinkOutcome.InvalidOrExpired => AccountLinkReason.InvalidOrExpired,
            BazaarDbLinkOutcome.AlreadyLinked => AccountLinkReason.AlreadyLinked,
            BazaarDbLinkOutcome.MissingFields => AccountLinkReason.MissingFields,
            BazaarDbLinkOutcome.ServerError => AccountLinkReason.ServerError,
            BazaarDbLinkOutcome.Transport => AccountLinkReason.Transport,
            _ => AccountLinkReason.UnexpectedOutcome,
        };
}
