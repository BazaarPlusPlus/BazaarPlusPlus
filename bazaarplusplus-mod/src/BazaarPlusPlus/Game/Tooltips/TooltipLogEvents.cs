#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

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
    EncounterProbeFailed,
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

internal enum TooltipPreviewRefreshMode
{
    Normal,
    Enchant,
    Upgrade,
}

internal enum TooltipCardPreviewOperation
{
    InvokeHover,
    InvokeHoverOut,
}

internal enum TooltipEncounterProbe
{
    Encounter,
}

[BppLogEventSource]
internal static class TooltipLogEvents
{
    internal static readonly BppLogFieldDefinition LevelRewardsOutcome = PublicLow(0, "outcome");
    internal static readonly BppLogFieldDefinition LevelRewardsReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition LevelRewardsLevel = PublicLow(2, "level");
    internal static readonly BppLogFieldDefinition LevelRewardsContentLength = PublicHigh(
        3,
        "content_length"
    );
    internal static readonly BppLogEventDefinition LevelRewardsRenderedOrSkipped = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.level_rewards.rendered_or_skipped",
        [LevelRewardsOutcome, LevelRewardsReasonCode, LevelRewardsLevel, LevelRewardsContentLength]
    );
    internal static readonly BppLogEventDefinition LevelRewardsDegraded = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.level_rewards.degraded",
        [LevelRewardsOutcome, LevelRewardsReasonCode, LevelRewardsLevel, LevelRewardsContentLength],
        new BppLogStormPolicy([LevelRewardsReasonCode])
    );

    internal static readonly BppLogFieldDefinition EncounterSectionReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition EncounterSectionDegraded = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.encounter_section.degraded",
        [EncounterSectionReasonCode],
        new BppLogStormPolicy([EncounterSectionReasonCode])
    );
    internal static readonly BppLogFieldDefinition EncounterInventoryReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition EncounterInventoryDegraded = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.encounter_inventory.degraded",
        [EncounterInventoryReasonCode],
        new BppLogStormPolicy([EncounterInventoryReasonCode])
    );

    internal static readonly BppLogFieldDefinition SectionDegradedSectionId = PublicLow(
        0,
        "section_id"
    );
    internal static readonly BppLogFieldDefinition SectionDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition SectionDegraded = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.section.degraded",
        [SectionDegradedSectionId, SectionDegradedReasonCode],
        new BppLogStormPolicy([SectionDegradedSectionId, SectionDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition SectionHostDegradedSectionId = PublicLow(
        0,
        "section_id"
    );
    internal static readonly BppLogFieldDefinition SectionHostDegradedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition SectionHostDegraded = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.section_host.degraded",
        [SectionHostDegradedSectionId, SectionHostDegradedReasonCode],
        new BppLogStormPolicy([SectionHostDegradedSectionId, SectionHostDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition PreviewRefreshReasonCode = PublicLow(
        0,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition PreviewRefreshMode = PublicLow(1, "mode");
    internal static readonly BppLogEventDefinition PreviewRefreshDegraded = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.preview_refresh.degraded",
        [PreviewRefreshReasonCode, PreviewRefreshMode],
        new BppLogStormPolicy([PreviewRefreshReasonCode])
    );
    internal static readonly BppLogFieldDefinition PreviewTargetOutcome = PublicLow(0, "outcome");
    internal static readonly BppLogFieldDefinition PreviewTargetReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogFieldDefinition PreviewTargetTemplateId = PublicHigh(
        2,
        "template_id"
    );
    internal static readonly BppLogFieldDefinition PreviewTargetCardInstanceId = HashedPublicHigh(
        3,
        "card_instance_id"
    );
    internal static readonly BppLogEventDefinition PreviewTargetResolvedOrSkipped = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.preview_target.resolved_or_skipped",
        [
            PreviewTargetOutcome,
            PreviewTargetReasonCode,
            PreviewTargetTemplateId,
            PreviewTargetCardInstanceId,
        ]
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
        BppLogFeatureScope.Tooltips,
        "tooltips.encounter_probe.degraded",
        [EncounterProbeDegradedProbe, EncounterProbeDegradedReasonCode],
        new BppLogStormPolicy([EncounterProbeDegradedProbe, EncounterProbeDegradedReasonCode])
    );
    internal static readonly BppLogFieldDefinition EncounterProbeRecoveredProbe = PublicLow(
        0,
        "probe"
    );
    internal static readonly BppLogEventDefinition EncounterProbeRecovered = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.encounter_probe.recovered",
        [EncounterProbeRecoveredProbe]
    );

    internal static readonly BppLogFieldDefinition CardPreviewHoverFailedOperation = PublicLow(
        0,
        "operation"
    );
    internal static readonly BppLogFieldDefinition CardPreviewHoverFailedReasonCode = PublicLow(
        1,
        "reason_code"
    );
    internal static readonly BppLogEventDefinition CardPreviewHoverFailed = new(
        BppLogFeatureScope.Tooltips,
        "tooltips.card_preview.hover_failed",
        [CardPreviewHoverFailedOperation, CardPreviewHoverFailedReasonCode]
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

    private static BppLogFieldDefinition HashedPublicHigh(int order, string name) =>
        new(
            order,
            name,
            BppLogFieldPrivacy.Public,
            BppLogCorrelationPolicy.Hash,
            BppLogCardinality.High
        );
}
