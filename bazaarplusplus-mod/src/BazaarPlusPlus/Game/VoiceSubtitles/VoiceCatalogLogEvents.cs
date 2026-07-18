#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

[BppLogEventSource]
internal static class VoiceCatalogLogEvents
{
    internal static readonly BppLogEventDefinition CatalogStarted = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.catalog.started",
        []
    );

    internal static readonly BppLogFieldDefinition CatalogReadySource = PublicField(
        0,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CatalogReadyLineCount = PublicField(
        1,
        "line_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CatalogReady = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.catalog.ready",
        [CatalogReadySource, CatalogReadyLineCount]
    );

    internal static readonly BppLogFieldDefinition CatalogDegradedReasonCode = PublicField(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CatalogDegradedSource = PublicField(
        1,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CatalogDegradedEndpoint = PublicField(
        2,
        "endpoint",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CatalogDegraded = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.catalog.degraded",
        [CatalogDegradedReasonCode, CatalogDegradedSource, CatalogDegradedEndpoint],
        new BppLogStormPolicy([CatalogDegradedReasonCode, CatalogDegradedSource])
    );

    internal static readonly BppLogFieldDefinition CatalogFailedReasonCode = PublicField(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CatalogFailedSource = PublicField(
        1,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CatalogFailed = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.catalog.failed",
        [CatalogFailedReasonCode, CatalogFailedSource]
    );

    internal static readonly BppLogFieldDefinition CatalogRefreshStartedReasonCode = PublicField(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CatalogRefreshStartedEndpoint = PublicField(
        1,
        "endpoint",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CatalogRefreshStarted = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.catalog_refresh.started",
        [CatalogRefreshStartedReasonCode, CatalogRefreshStartedEndpoint]
    );

    internal static readonly BppLogFieldDefinition CatalogRecoveredReasonCode = PublicField(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CatalogRecoveredSource = PublicField(
        1,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CatalogRecoveredLineCount = PublicField(
        2,
        "line_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CatalogRecovered = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.catalog.recovered",
        [CatalogRecoveredReasonCode, CatalogRecoveredSource, CatalogRecoveredLineCount]
    );

    internal static readonly BppLogFieldDefinition CatalogCacheDegradedReasonCode = PublicField(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CatalogCacheDegraded = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.catalog_cache.degraded",
        [CatalogCacheDegradedReasonCode],
        new BppLogStormPolicy([CatalogCacheDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition CatalogRowSkippedSource = PublicField(
        0,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CatalogRowSkippedRowNumber = PublicField(
        1,
        "row_number",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CatalogRowSkippedReasonCode = PublicField(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CatalogRowSkippedStem = new(
        3,
        "stem",
        BppLogFieldPrivacy.UntrustedText,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CatalogRowSkipped = new(
        BppLogFeatureScope.VoiceSubtitles,
        "voice_subtitles.catalog_row.skipped",
        [
            CatalogRowSkippedSource,
            CatalogRowSkippedRowNumber,
            CatalogRowSkippedReasonCode,
            CatalogRowSkippedStem,
        ]
    );

    private static BppLogFieldDefinition PublicField(
        int order,
        string name,
        BppLogCardinality cardinality
    ) => new(order, name, BppLogFieldPrivacy.Public, BppLogCorrelationPolicy.None, cardinality);
}
