#nullable enable
using BazaarPlusPlus.Infrastructure;
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
/// Owns one accepted account-link operation's correlation and single terminal diagnostic. A panel
/// session cancellation calls <see cref="Abandon"/>, which terminalizes the operation silently.
/// </summary>
internal sealed class AccountLinkLogRequest
{
    private readonly string _requestId;
    private readonly AccountLinkMethod _method;
    private int _terminal;

    internal AccountLinkLogRequest(string requestId, AccountLinkMethod method)
    {
        _requestId = string.IsNullOrWhiteSpace(requestId)
            ? throw new ArgumentException("Request ID is required.", nameof(requestId))
            : requestId;
        _method = method;
    }

    internal void Succeeded()
    {
        if (!TryComplete())
            return;

        BppLog.InfoEvent(
            HistoryPanelAccountLinkLogEvents.Succeeded,
            new[]
            {
                HistoryPanelAccountLinkLogEvents.RequestId.Bind(_requestId),
                HistoryPanelAccountLinkLogEvents.Method.Bind(_method),
            }
        );
    }

    internal void Failed(BazaarDbLinkOutcome outcome, Exception? exception = null) =>
        Failed(MapFailure(outcome), exception);

    internal void Failed(AccountLinkReason reason, Exception? exception = null)
    {
        if (!TryComplete())
            return;

        var values = new[]
        {
            HistoryPanelAccountLinkLogEvents.RequestId.Bind(_requestId),
            HistoryPanelAccountLinkLogEvents.Method.Bind(_method),
            HistoryPanelAccountLinkLogEvents.FailureReasonCode.Bind(reason),
        };
        if (exception == null)
            BppLog.ErrorEvent(HistoryPanelAccountLinkLogEvents.Failed, values);
        else
            BppLog.ErrorEvent(HistoryPanelAccountLinkLogEvents.Failed, exception, values);
    }

    internal void Skipped(AccountLinkReason reason)
    {
        if (!TryComplete())
            return;

        BppLog.DebugEvent(
            HistoryPanelAccountLinkLogEvents.Skipped,
            () =>
                new[]
                {
                    HistoryPanelAccountLinkLogEvents.RequestId.Bind(_requestId),
                    HistoryPanelAccountLinkLogEvents.SkippedReasonCode.Bind(reason),
                }
        );
    }

    internal void Abandon()
    {
        TryComplete();
    }

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

    private bool TryComplete() => Interlocked.CompareExchange(ref _terminal, 1, 0) == 0;
}
