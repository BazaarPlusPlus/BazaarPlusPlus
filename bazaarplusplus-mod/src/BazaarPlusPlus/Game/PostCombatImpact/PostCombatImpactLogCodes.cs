#nullable enable

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
