#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

internal enum ItemEnchantRenderStage
{
    CardTooltipData,
    TooltipBuilder,
    Localization,
}

internal enum ItemEnchantLogReasonCode
{
    RenderFallback,
    RawTextFallback,
    LocalizationFallback,
    EncounterProbeFailed,
}

internal enum ItemEnchantEncounterProbe
{
    Encounter,
}

[BppLogEventSource]
internal static class ItemEnchantPreviewLogEvents
{
    internal static readonly BppLogFieldDefinition RenderDegradedStage = PublicLow(0, "stage");
    internal static readonly BppLogFieldDefinition RenderDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition RenderDegradedEnchantment = PublicLow(
        2,
        "enchantment"
    );
    internal static readonly BppLogEventDefinition RenderDegraded = new(
        BppLogFeatureScope.ItemEnchantPreview,
        "item_enchant_preview.render.degraded",
        [RenderDegradedStage, RenderDegradedReasonCode, RenderDegradedEnchantment],
        new BppLogStormPolicy([RenderDegradedStage, RenderDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition EncounterProbeDegradedProbe = PublicLow(
        0,
        "probe"
    );
    internal static readonly BppLogFieldDefinition EncounterProbeDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition EncounterProbeDegraded = new(
        BppLogFeatureScope.ItemEnchantPreview,
        "item_enchant_preview.encounter_probe.degraded",
        [EncounterProbeDegradedProbe, EncounterProbeDegradedReasonCode],
        new BppLogStormPolicy([EncounterProbeDegradedProbe, EncounterProbeDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition EncounterProbeRecoveredProbe = PublicLow(
        0,
        "probe"
    );
    internal static readonly BppLogEventDefinition EncounterProbeRecovered = new(
        BppLogFeatureScope.ItemEnchantPreview,
        "item_enchant_preview.encounter_probe.recovered",
        [EncounterProbeRecoveredProbe]
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
