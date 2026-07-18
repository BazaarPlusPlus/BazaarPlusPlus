#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.GameInterop.Localization;

internal enum BilingualLogReasonCode
{
    DatabaseUnavailable,
    QueryException,
    TooltipPatchException,
}

[BppLogEventSource]
internal static class BilingualItemNamesLogEvents
{
    internal static readonly BppLogFieldDefinition CatalogDegradedLocale = PublicLow(0, "locale");
    internal static readonly BppLogFieldDefinition CatalogDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition CatalogDegraded = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.catalog.degraded",
        [CatalogDegradedLocale, CatalogDegradedReasonCode],
        new BppLogStormPolicy([CatalogDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition CatalogRecoveredLocale = PublicLow(0, "locale");
    internal static readonly BppLogEventDefinition CatalogRecovered = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.catalog.recovered",
        [CatalogRecoveredLocale]
    );
    internal static readonly BppLogFieldDefinition CatalogLoadedLocale = PublicLow(0, "locale");
    internal static readonly BppLogEventDefinition CatalogLoaded = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.catalog.loaded",
        [CatalogLoadedLocale]
    );

    internal static readonly BppLogFieldDefinition TooltipDegradedReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition TooltipDegraded = new(
        BppLogFeatureScope.BilingualItemNames,
        "bilingual_item_names.tooltip.degraded",
        [TooltipDegradedReasonCode],
        new BppLogStormPolicy([TooltipDegradedReasonCode])
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.None,
            BppLogCardinality.Low
        );
}
