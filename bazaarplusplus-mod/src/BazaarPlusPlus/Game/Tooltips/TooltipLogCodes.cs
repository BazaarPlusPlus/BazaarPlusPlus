#nullable enable

namespace BazaarPlusPlus.Game.Tooltips;

internal enum TooltipLogReasonCode
{
    NotHeroLevelData,
    NoContent,
    Rendered,
    SectionUnavailable,
    RenderException,
    InventoryReadException,
    PassiveEffectParentUnavailable,
    TextControllerUnavailable,
    PreviewRefreshException,
    NoPrimaryController,
    NoItemTooltipData,
    NoCardController,
    ControllerCardMismatch,
    PreviewCardMatched,
    PrimaryCardMatched,
    ReflectionUnavailable,
}

internal enum TooltipLevelRewardsOutcome
{
    Skipped,
    Rendered,
    SectionUnavailable,
    Failed,
}

internal enum TooltipSectionId
{
    Unknown,
    EnchantPreview,
    QuestRewardPreview,
    AggregateMissingTypes,
    EncounterPreview,
    HeroLevelRewards,
}

internal enum TooltipPreviewTargetOutcome
{
    Skipped,
    Resolved,
}

internal enum TooltipEncounterProbe
{
    Encounter,
}
