#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

[BppLogEventSource]
internal static class ScreenshotUploadLogEvents
{
    internal static readonly BppLogFieldDefinition InitializationDegradedReasonCode = Field(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition InitializationDegradedEndpoint = Field(
        1,
        "endpoint",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition InitializationDegraded = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.upload.initialization_degraded",
        [InitializationDegradedReasonCode, InitializationDegradedEndpoint],
        new BppLogStormPolicy([InitializationDegradedEndpoint, InitializationDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition WaitingReasonCode = Field(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition WaitingPendingCount = Field(
        1,
        "pending_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition Waiting = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.upload.waiting",
        [WaitingReasonCode, WaitingPendingCount]
    );

    internal static readonly BppLogFieldDefinition DegradedEndpoint = Field(
        0,
        "endpoint",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DegradedReasonCode = Field(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DegradedRttMilliseconds = Field(
        2,
        "rtt_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition Degraded = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.upload.degraded",
        [DegradedEndpoint, DegradedReasonCode, DegradedRttMilliseconds],
        new BppLogStormPolicy([DegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition RecoveredEndpoint = Field(
        0,
        "endpoint",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition RecoveredReasonCode = Field(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition RecoveredOutageDurationMilliseconds = Field(
        2,
        "outage_duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition Recovered = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.upload.recovered",
        [RecoveredEndpoint, RecoveredReasonCode, RecoveredOutageDurationMilliseconds]
    );

    private static BppLogFieldDefinition Field(
        int order,
        string name,
        BppLogCardinality cardinality
    ) => new(order, name, BppLogFieldPrivacy.Public, BppLogCorrelationPolicy.None, cardinality);
}
