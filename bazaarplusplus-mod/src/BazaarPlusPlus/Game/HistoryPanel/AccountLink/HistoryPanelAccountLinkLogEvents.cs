#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.HistoryPanel.AccountLink;

[BppLogEventSource]
internal static class HistoryPanelAccountLinkLogEvents
{
    internal static readonly BppLogFieldDefinition RequestId = new(
        0,
        "request_id",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.Short,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition Method = new(
        1,
        "method",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition FailureReasonCode = new(
        2,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition SkippedReasonCode = new(
        1,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogEventDefinition Failed = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.account_link.failed",
        new[] { RequestId, Method, FailureReasonCode },
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );

    internal static readonly BppLogEventDefinition Succeeded = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.account_link.succeeded",
        new[] { RequestId, Method }
    );

    internal static readonly BppLogEventDefinition Skipped = new(
        BppLogFeatureScope.HistoryPanel,
        "history_panel.account_link.skipped",
        new[] { RequestId, SkippedReasonCode }
    );
}
