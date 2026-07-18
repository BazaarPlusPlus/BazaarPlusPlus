#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.EventPreview;

internal enum EventPreviewPlanSource
{
    Unknown,
    Cache,
    Rebuild,
}

internal enum EventPreviewPlanReasonCode
{
    None,
    SourceInfoUnavailable,
    LoadException,
    PartialCoverage,
    CacheWriteException,
}

[BppLogEventSource]
internal static class EventPreviewLogEvents
{
    internal static readonly BppLogFieldDefinition Source = PublicLow(0, "source");
    internal static readonly BppLogFieldDefinition ReasonCode = PublicLow(1, "reason_code");
    internal static readonly BppLogFieldDefinition EventCount = PublicHigh(2, "event_count");
    internal static readonly BppLogFieldDefinition LevelUpCount = PublicHigh(3, "level_up_count");
    internal static readonly BppLogFieldDefinition TemplateCount = PublicHigh(4, "template_count");
    internal static readonly BppLogFieldDefinition EventFailureCount = PublicHigh(
        5,
        "event_failure_count"
    );
    internal static readonly BppLogFieldDefinition LevelUpFailureCount = PublicHigh(
        6,
        "level_up_failure_count"
    );
    internal static readonly BppLogFieldDefinition UnsupportedLevelUpPartCount = PublicHigh(
        7,
        "unsupported_level_up_part_count"
    );
    internal static readonly BppLogFieldDefinition MissingTemplateCount = PublicHigh(
        8,
        "missing_template_count"
    );
    internal static readonly BppLogFieldDefinition SizeBytes = PublicHigh(9, "size_bytes");
    internal static readonly BppLogFieldDefinition LoadDurationMs = PublicHigh(
        10,
        "load_duration_ms"
    );
    internal static readonly BppLogFieldDefinition CompileDurationMs = PublicHigh(
        11,
        "compile_duration_ms"
    );
    internal static readonly BppLogFieldDefinition WriteDurationMs = PublicHigh(
        12,
        "write_duration_ms"
    );
    internal static readonly BppLogFieldDefinition CachePath = LocalPath(13, "cache_path");

    private static readonly BppLogFieldDefinition[] Fields =
    [
        Source,
        ReasonCode,
        EventCount,
        LevelUpCount,
        TemplateCount,
        EventFailureCount,
        LevelUpFailureCount,
        UnsupportedLevelUpPartCount,
        MissingTemplateCount,
        SizeBytes,
        LoadDurationMs,
        CompileDurationMs,
        WriteDurationMs,
        CachePath,
    ];

    internal static readonly BppLogEventDefinition PlansLoadFailed = new(
        BppLogFeatureScope.EventPreview,
        "event_preview.plans.load_failed",
        Fields
    );
    internal static readonly BppLogEventDefinition PlansDegraded = new(
        BppLogFeatureScope.EventPreview,
        "event_preview.plans.degraded",
        Fields,
        new BppLogStormPolicy([Source, ReasonCode])
    );
    internal static readonly BppLogEventDefinition PlansReady = new(
        BppLogFeatureScope.EventPreview,
        "event_preview.plans.ready",
        Fields
    );
    internal static readonly BppLogEventDefinition PlansRecovered = new(
        BppLogFeatureScope.EventPreview,
        "event_preview.plans.recovered",
        Fields
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.Low
        );

    private static BppLogFieldDefinition PublicHigh(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );

    private static BppLogFieldDefinition LocalPath(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.LocalPath,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.High
        );
}
