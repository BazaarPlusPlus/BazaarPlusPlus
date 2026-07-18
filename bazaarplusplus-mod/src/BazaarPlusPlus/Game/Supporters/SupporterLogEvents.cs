#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Supporters;

internal enum SupporterCatalogSource
{
    Remote,
    DiskCache,
    BundledFallback,
}

internal enum SupporterLogReasonCode
{
    EmptyPayload,
    ReadException,
    RefreshException,
    WriteException,
}

internal enum SupporterCacheOperation
{
    Write,
}

internal enum SupporterCatalogOperation
{
    Load,
}

internal readonly record struct SupporterCatalogFailure(
    SupporterCatalogSource Source,
    SupporterLogReasonCode Reason
);

[BppLogEventSource]
internal static class SupporterLogEvents
{
    internal static readonly BppLogFieldDefinition CatalogDegradedSource = PublicLow(0, "source");
    internal static readonly BppLogFieldDefinition CatalogDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition CatalogDegradedCachePath = LocalPath(
        2,
        "cache_path"
    );
    internal static readonly BppLogEventDefinition CatalogDegraded = new(
        BppLogFeatureScope.Supporters,
        "supporters.catalog.degraded",
        [CatalogDegradedSource, CatalogDegradedReasonCode, CatalogDegradedCachePath],
        new BppLogStormPolicy([CatalogDegradedSource, CatalogDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition CatalogRecoveredSource = PublicLow(0, "source");
    internal static readonly BppLogFieldDefinition CatalogRecoveredEntryCount = PublicHigh(
        1,
        "entry_count"
    );
    internal static readonly BppLogEventDefinition CatalogRecovered = new(
        BppLogFeatureScope.Supporters,
        "supporters.catalog.recovered",
        [CatalogRecoveredSource, CatalogRecoveredEntryCount]
    );

    internal static readonly BppLogFieldDefinition CatalogLoadedSource = PublicLow(0, "source");
    internal static readonly BppLogFieldDefinition CatalogLoadedEntryCount = PublicHigh(
        1,
        "entry_count"
    );
    internal static readonly BppLogEventDefinition CatalogLoaded = new(
        BppLogFeatureScope.Supporters,
        "supporters.catalog.loaded",
        [CatalogLoadedSource, CatalogLoadedEntryCount]
    );

    internal static readonly BppLogFieldDefinition CacheWriteDegradedPath = LocalPath(0, "path");
    internal static readonly BppLogFieldDefinition CacheWriteDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition CacheWriteDegraded = new(
        BppLogFeatureScope.Supporters,
        "supporters.cache.write_degraded",
        [CacheWriteDegradedPath, CacheWriteDegradedReasonCode],
        new BppLogStormPolicy([CacheWriteDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition CacheWriteRecoveredPath = LocalPath(0, "path");
    internal static readonly BppLogEventDefinition CacheWriteRecovered = new(
        BppLogFeatureScope.Supporters,
        "supporters.cache.write_recovered",
        [CacheWriteRecoveredPath]
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
