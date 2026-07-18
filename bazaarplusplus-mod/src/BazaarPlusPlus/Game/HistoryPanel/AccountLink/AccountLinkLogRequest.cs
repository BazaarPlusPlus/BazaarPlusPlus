#nullable enable
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

internal interface IHistoryPanelAccountLinkLogSink
{
    void Emit(
        BppLogSeverity severity,
        BppLogEventDefinition definition,
        BppLogFieldValue[] values,
        Exception? exception
    );

    void EmitDebug(BppLogEventDefinition definition, Func<BppLogFieldValue[]> valuesFactory);
}

/// <summary>
/// Owns one accepted account-link operation's correlation and single terminal diagnostic. A panel
/// session cancellation calls <see cref="Abandon"/>, which terminalizes the operation silently.
/// </summary>
internal sealed class AccountLinkLogRequest
{
    private readonly string _requestId;
    private readonly AccountLinkMethod _method;
    private readonly IHistoryPanelAccountLinkLogSink _sink;
    private int _terminal;

    internal AccountLinkLogRequest(
        string requestId,
        AccountLinkMethod method,
        IHistoryPanelAccountLinkLogSink sink
    )
    {
        _requestId = string.IsNullOrWhiteSpace(requestId)
            ? throw new ArgumentException("Request ID is required.", nameof(requestId))
            : requestId;
        _method = method;
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    internal void Succeeded()
    {
        if (!TryComplete())
            return;

        Emit(
            BppLogSeverity.Info,
            HistoryPanelAccountLinkLogEvents.Succeeded,
            new[]
            {
                HistoryPanelAccountLinkLogEvents.RequestId.Bind(_requestId),
                HistoryPanelAccountLinkLogEvents.Method.Bind(_method),
            },
            exception: null
        );
    }

    internal void Failed(BazaarDbLinkOutcome outcome) => Failed(MapFailure(outcome));

    internal void Failed(AccountLinkReason reason, Exception? exception = null)
    {
        if (!TryComplete())
            return;

        Emit(
            BppLogSeverity.Error,
            HistoryPanelAccountLinkLogEvents.Failed,
            new[]
            {
                HistoryPanelAccountLinkLogEvents.RequestId.Bind(_requestId),
                HistoryPanelAccountLinkLogEvents.Method.Bind(_method),
                HistoryPanelAccountLinkLogEvents.FailureReasonCode.Bind(reason),
            },
            exception
        );
    }

    internal void Skipped(AccountLinkReason reason)
    {
        if (!TryComplete())
            return;

#if DEBUG
        try
        {
            _sink.EmitDebug(
                HistoryPanelAccountLinkLogEvents.Skipped,
                () =>
                    new[]
                    {
                        HistoryPanelAccountLinkLogEvents.RequestId.Bind(_requestId),
                        HistoryPanelAccountLinkLogEvents.SkippedReasonCode.Bind(reason),
                    }
            );
        }
        catch
        {
            // Operational logging must never alter account-link behavior.
        }
#endif
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

    private void Emit(
        BppLogSeverity severity,
        BppLogEventDefinition definition,
        BppLogFieldValue[] values,
        Exception? exception
    )
    {
        try
        {
            _sink.Emit(severity, definition, values, exception);
        }
        catch
        {
            // Operational logging must never alter account-link behavior.
        }
    }
}
