#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.PostCombatImpact;

internal enum PostCombatImpactReasonCode
{
    ProjectionException,
    ProjectionAttributionGap,
    Shown,
    ShownWithoutAttributedImpact,
    RuntimeUnavailable,
    TooltipDataUnavailable,
    SourceIdUnavailable,
    PrimaryTooltipCreateTimedOut,
    AuxiliaryTooltipCreateTimedOut,
    AuxiliaryTooltipContentUnavailable,
    AuxiliaryTooltipShowRetried,
    AuxiliaryTooltipPositionUnavailable,
    TypographyUnavailable,
    PairOpenMissingAuxiliaryFields,
    PairOpenDyingController,
    PairOpenMissingBackground,
    PairOpenBackgroundCloneRejected,
    NativeAuxiliaryDisplaced,
    NativeAuxiliaryHidden,
    NativeAuxiliaryRequeued,
    TooltipRenderException,
    Dismissed,
    RecapHoverObserved,
    StaleRequestDiscarded,
    PendingShowBlocked,
    PendingShowAborted,
    NativeAuxiliaryUnmatched,
    PairPlacementOverflowed,
    PairPlacementTooNarrow,
    PairTopAlignmentAdjusted,
    PrimaryGeometrySettleTimedOut,
    PairGeometrySettleTimedOut,
    PerspectiveReceived,
    PerspectiveCaused,
    EntityPreviewUnavailable,
    EntityPreviewCreateTimedOut,
}

internal enum PostCombatImpactHoverExitOrigin
{
    RecapPointerExit,
    RecapDisabled,
    SkillPointerExit,
}

internal enum NativeAuxiliaryTooltipAnomalyCategory
{
    RequestedTextInactive,
    VisibleWithoutText,
}

internal enum NativeAuxiliaryTooltipAnomalyPhase
{
    ShowHandoff,
    FrameAudit,
}

[BppLogEventSource]
internal static class PostCombatImpactLogEvents
{
    internal static readonly BppLogFieldDefinition ReasonCode = new(
        0,
        "reason_code",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition ProjectionCategory = new(
        3,
        "projection_category",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogEventDefinition ProjectionDegraded = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.projection.degraded",
        [ReasonCode, ProjectionCategory],
        new BppLogStormPolicy([ReasonCode, ProjectionCategory])
    );

    internal static readonly BppLogEventDefinition InteractionObserved = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.interaction.observed",
        [ReasonCode]
    );

    internal static readonly BppLogFieldDefinition Card = new(
        1,
        "card",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition Detail = new(
        2,
        "detail",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    /// <summary>
    /// Card-identified interaction trail. Exists because the plain observed trail proved unable
    /// to attribute silent per-card failures: it shows a hover with no outcome but not which card
    /// or which silent gate swallowed it.
    /// </summary>
    internal static readonly BppLogEventDefinition InteractionTraced = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.interaction.traced",
        [ReasonCode, Card, Detail]
    );

    internal static readonly BppLogEventDefinition InteractionDegraded = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.interaction.degraded",
        [ReasonCode],
        new BppLogStormPolicy([ReasonCode])
    );

    internal static readonly BppLogFieldDefinition AnomalyCategory = PublicLow(0, "category");
    internal static readonly BppLogFieldDefinition AnomalyPhase = PublicLow(1, "phase");
    internal static readonly BppLogFieldDefinition HeaderActive = PublicLow(2, "header_active");
    internal static readonly BppLogFieldDefinition BodyActive = PublicLow(3, "body_active");
    internal static readonly BppLogFieldDefinition HeaderEmpty = PublicLow(4, "header_empty");
    internal static readonly BppLogFieldDefinition BodyEmpty = PublicLow(5, "body_empty");
    internal static readonly BppLogFieldDefinition PairedContentActive = PublicLow(
        6,
        "paired_content_active"
    );
    internal static readonly BppLogFieldDefinition Recovered = PublicLow(7, "recovered");

    internal static readonly BppLogEventDefinition NativeAuxiliaryAnomaly = new(
        BppLogFeatureScope.PostCombatImpact,
        "post_combat_impact.native_auxiliary.anomaly",
        [
            AnomalyCategory,
            AnomalyPhase,
            HeaderActive,
            BodyActive,
            HeaderEmpty,
            BodyEmpty,
            PairedContentActive,
            Recovered,
        ],
        new BppLogStormPolicy([AnomalyCategory, AnomalyPhase])
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(order, name, BppLogCorrelationPolicy.None, BppLogCardinality.Low);
}
