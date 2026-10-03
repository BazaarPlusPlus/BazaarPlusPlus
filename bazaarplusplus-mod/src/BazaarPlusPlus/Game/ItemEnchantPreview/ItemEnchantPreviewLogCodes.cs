#nullable enable

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
}

internal enum ItemEnchantEncounterProbe
{
    Encounter,
}
