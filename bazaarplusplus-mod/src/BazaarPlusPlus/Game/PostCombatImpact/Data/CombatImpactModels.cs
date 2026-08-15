#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.Actions;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal enum CombatImpactKind
{
    DirectDamage,
    Burn,
    Poison,
    Healing,
    Shield,
    Charge,
    Haste,
    Slow,
    Freeze,
    Flying,
    AttributeChange,
    Destroy,
}

internal enum CombatImpactValueUnit
{
    Amount,
    Milliseconds,
    PercentagePoints,
    Applications,
}

internal enum CombatImpactEventSurface
{
    AppliedEffect,
    CardAttribute,
    PlayerAttribute,
}

internal enum CombatImpactValueBasis
{
    None,
    ExactAdjustment,
    ConfiguredActionAmount,
    NetFrameDelta,
}

internal enum CombatImpactOccurrenceBasis
{
    ReconstructedTransition,
    ExplicitExecution,
}

internal enum CombatImpactActivitySourceResolution
{
    Direct,
    TriggerFallback,
    PrerequisiteSkill,
}

internal enum CombatImpactTriggerScope
{
    AttributedExternal,
    AttributedSelf,
    AttributedViaTriggerFallback,
    NoTriggerEvidence,
    Unattributed,
    NotApplicable,
}

internal enum CombatImpactTriggerPresentationState
{
    None,
    Complete,
    PartialBreakdown,
    HiddenSelfOnly,
    BreakdownUnavailable,
}

internal enum CombatImpactCoverage
{
    None,
    Exact,
    Estimated,
    LowerBound,
    Partial,
}

internal enum CombatImpactAuthoritativeBasis
{
    TotalAmount,
    ApplicationCount,
}

internal enum CombatImpactPeriodicKind
{
    Burn,
    Poison,
    Regen,
}

internal enum CombatImpactPeriodicProof
{
    Exact,
    Proportional,
}

internal enum CombatImpactControlStatus
{
    Exact,
    PositiveResidual,
    OverObserved,
    NotComparable,
}

internal enum CombatImpactResidualCoverage
{
    Unknown,
    Exact,
    UpperBound,
}

internal enum CombatImpactAttributeTransitionResidualReason
{
    ConcurrentAttributionUnavailable,
}

internal enum CombatImpactAttributeTransitionResolution
{
    SingleClaimantNet,
    ConcurrentConfiguredExact,
    ConcurrentSingleUnknownSolved,
    ConcurrentResidual,
}

internal enum CombatImpactAttributeTransitionFailureReason
{
    ModifierUnavailable,
    AttributeMismatch,
    MultiplyOperation,
    AdditiveMultiplyOperation,
    UnsupportedOperation,
    RangeValue,
    CardAttributeUnscaled,
    AttributeChangeReference,
    PlayerAttributeReference,
    CardCountReference,
    NonSelfCardReference,
    SourceAttributeUnavailable,
    DynamicReferenceModifier,
    NonIntegralConfiguredValue,
    UnsupportedValueType,
    ConfiguredSumMismatch,
    ClaimantIdentityMismatch,
}

internal readonly record struct PeriodicImpactKey(
    string SourceId,
    ECombatantId Combatant,
    CombatImpactPeriodicKind Kind
);

internal sealed record CombatImpactPeriodicImpact(
    int HealthAmount,
    int ShieldAmount,
    CombatImpactPeriodicProof Proof,
    string ModelVersion
);

internal sealed record CombatImpactPrerequisiteSkillSourceRule(
    string EffectId,
    Guid SkillTemplateId,
    IReadOnlyCollection<ETier> SkillTiers
)
{
    internal bool Matches(CombatImpactEntity entity) =>
        entity.TypeLabel == "Skill"
        && entity.TemplateId == SkillTemplateId
        && (SkillTiers.Count == 0 || SkillTiers.Contains(entity.Tier));
}

internal sealed record CombatImpactItemTagCondition(
    EListComparisonOperator Operator,
    IReadOnlyCollection<ECardTag>? PublicTags = null,
    IReadOnlyCollection<EHiddenTag>? HiddenTags = null
)
{
    internal bool Matches(CombatImpactEntity entity)
    {
        if (PublicTags is { Count: > 0 })
            return Matches(PublicTags, entity.Tags);
        if (HiddenTags is { Count: > 0 })
            return Matches(HiddenTags, entity.HiddenTags);
        return false;
    }

    private bool Matches<T>(IReadOnlyCollection<T> required, IReadOnlyCollection<T>? actual)
        where T : struct, Enum
    {
        var present = actual == null ? new HashSet<T>() : new HashSet<T>(actual);
        return Operator switch
        {
            EListComparisonOperator.All => required.All(present.Contains),
            EListComparisonOperator.Any => required.Any(present.Contains),
            EListComparisonOperator.None => required.All(tag => !present.Contains(tag)),
            _ => false,
        };
    }
}

internal sealed record CombatImpactUseAttributionRule(
    CombatImpactPrerequisiteSkillSourceRule SourceRule,
    CombatImpactItemTagCondition ItemCondition,
    int FixedTempoAmount
);

internal enum CombatImpactProjectionDiagnosticKind
{
    MissingPrerequisiteSkill,
    AmbiguousPrerequisiteSkill,
    MissingUseCount,
    ExplicitApplicationsExceedUseCount,
    RuleExecutionMismatch,
    NonDisplayableAuthoritativeMetric,
}

internal sealed record CombatImpactProjectionDiagnostic(
    CombatImpactProjectionDiagnosticKind Kind,
    string SourceId,
    string EffectId,
    string? TriggerSourceId = null
);

internal sealed record CombatImpactEntity(
    string Id,
    string Name,
    string TypeLabel,
    EHero? Hero,
    int Order,
    Guid TemplateId = default,
    ETier Tier = ETier.Bronze,
    int DisplaySpan = 1,
    EEnchantmentType? EnchantmentType = null,
    IReadOnlyDictionary<ECardAttributeType, int>? Attributes = null,
    ECombatantId? CombatantId = null,
    IReadOnlyDictionary<string, ECardAttributeType>? AbilityAttributeTypesByEffectId = null,
    IReadOnlyDictionary<string, ECardAttributeType>? AuraAttributeTypesByEffectId = null,
    IReadOnlyCollection<string>? ReferenceValuedAuraEffectIds = null,
    EContainerSocketId? SocketId = null,
    IReadOnlyCollection<EHiddenTag>? HiddenTags = null,
    EInventorySection? Section = null,
    IReadOnlyCollection<ECardTag>? Tags = null,
    IReadOnlyDictionary<
        string,
        CombatImpactPrerequisiteSkillSourceRule
    >? PrerequisiteSkillSourceRulesByEffectId = null,
    IReadOnlyCollection<CombatImpactUseAttributionRule>? UseAttributionRules = null,
    IReadOnlyDictionary<string, TActionCardModifyAttribute>? AbilityAttributeModifiersByEffectId =
        null,
    IReadOnlyDictionary<string, EEffectPriority>? CriticalTriggerAbilitiesByEffectId = null,
    IReadOnlyCollection<string>? CritCapableEffectIds = null
);

internal static class CombatImpactTags
{
    internal static IReadOnlyCollection<ECardTag>? Merge(
        IReadOnlyCollection<ECardTag>? runtime,
        IReadOnlyCollection<ECardTag>? template,
        IReadOnlyCollection<ECardTag>? enchantment
    )
    {
        var tags = new HashSet<ECardTag>();
        if (runtime != null)
            tags.UnionWith(runtime);
        if (template != null)
            tags.UnionWith(template);
        if (enchantment != null)
            tags.UnionWith(enchantment);
        return tags.Count == 0 ? null : tags.ToArray();
    }
}

internal static class CombatImpactHiddenTags
{
    internal static IReadOnlyCollection<EHiddenTag>? Merge(
        IReadOnlyCollection<EHiddenTag>? runtime,
        IReadOnlyCollection<EHiddenTag>? template,
        IReadOnlyCollection<EHiddenTag>? enchantment
    )
    {
        var hiddenTags = new HashSet<EHiddenTag>();
        if (runtime != null)
            hiddenTags.UnionWith(runtime);
        if (template != null)
            hiddenTags.UnionWith(template);
        if (enchantment != null)
            hiddenTags.UnionWith(enchantment);
        return hiddenTags.Count == 0 ? null : hiddenTags.ToArray();
    }
}

internal sealed record CombatImpactEvent(
    CombatImpactKind Kind,
    string SourceId,
    string TargetId,
    int? Value = null,
    CombatImpactValueUnit Unit = CombatImpactValueUnit.Amount,
    string? NativeAttributeKey = null,
    bool IsCritical = false,
    CombatImpactValueBasis ValueBasis = CombatImpactValueBasis.ExactAdjustment
)
{
    internal CombatImpactEventSurface Surface { get; init; } =
        CombatImpactEventSurface.AppliedEffect;

    internal CombatImpactOccurrenceBasis OccurrenceBasis { get; init; } =
        CombatImpactOccurrenceBasis.ReconstructedTransition;

    internal int CriticalCount { get; init; }

    /// <summary>
    /// Number of known critical/non-critical outcomes represented by this event. Exact aggregate
    /// recovery may store several outcomes on one event when their individual identities are lost.
    /// </summary>
    internal int CriticalOutcomeCount { get; init; }

    internal int? CriticalValue { get; init; }

    internal int? NonCriticalValue { get; init; }

    internal int? AlternateNonCriticalValue { get; init; }

    internal bool HasCriticalAdjustmentCandidate { get; init; }

    internal bool IsCritCapable { get; init; }

    internal string? RawDirectSourceId { get; init; }

    internal string? TriggerSourceId { get; init; }

    internal int? TriggerFrameIndex { get; init; }

    internal CombatImpactActivitySourceResolution ActivitySourceResolution { get; init; } =
        CombatImpactActivitySourceResolution.Direct;

    internal CombatImpactTriggerScope TriggerScope { get; init; } =
        CombatImpactTriggerScope.NotApplicable;

    internal bool IsUnattributedTransitionClaimant { get; init; }

    internal int? UnattributedTransitionValue { get; init; }

    internal int? AttributeTransitionNetValue { get; init; }

    internal CombatImpactAttributeTransitionResolution? AttributeTransitionResolution { get; init; }

    internal IReadOnlyList<CombatImpactAttributeTransitionFailureReason> AttributeTransitionFailureReasons { get; init; } =
    [];

    internal int AttributeTransitionUnresolvedClaimantCount { get; init; }

    internal bool AttributeTransitionEventOrderReplayReconciles { get; init; }

    internal bool AttributeTransitionEventOrderReplayIncludesMultiply { get; init; }
}

internal sealed record CombatImpactAttributeTransitionResidual(
    int FrameIndex,
    string TargetId,
    string NativeAttributeKey,
    int Value,
    CombatImpactValueUnit Unit,
    int ApplicationCount,
    CombatImpactAttributeTransitionResidualReason Reason
)
{
    internal CombatImpactKind Kind => CombatImpactKind.AttributeChange;

    internal CombatImpactEventSurface Surface => CombatImpactEventSurface.CardAttribute;
}

internal sealed record CombatImpactApplicationLedger(
    int ProjectedApplicationCount,
    int? AuthoritativeApplicationCount,
    bool ComparableToAuthoritativeCount,
    int? ApplicationResidual,
    CombatImpactControlStatus ControlStatus
);

internal sealed record CombatImpactAmountLedger(
    int? AuthoritativeTotal,
    int? ObservedAmount,
    CombatImpactCoverage ObservedCoverage,
    int ValuedApplicationCount,
    int TotalApplicationCount,
    CombatImpactValueUnit Unit,
    CombatImpactValueBasis WeakestValueBasis,
    bool ComparableToAuthoritativeTotal,
    long? ResidualAmount,
    CombatImpactResidualCoverage ResidualCoverage,
    CombatImpactControlStatus ControlStatus
);

internal sealed record CombatImpactTarget(
    CombatImpactEntity Entity,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage
)
{
    internal int ValuedApplicationCount { get; init; }

    internal bool HasUnattributedTransitionValue { get; init; }
}

internal sealed record CombatImpactAuthoritativeMetric
{
    internal CombatImpactAuthoritativeMetric(
        CombatImpactKind kind,
        string nativeAttributeKey,
        int value,
        CombatImpactValueUnit unit,
        CombatImpactAuthoritativeBasis basis,
        bool canReconcileApplicationCount = false
    )
    {
        var valid = basis switch
        {
            CombatImpactAuthoritativeBasis.TotalAmount => unit == CombatImpactValueUnit.Amount,
            CombatImpactAuthoritativeBasis.ApplicationCount => unit
                == CombatImpactValueUnit.Applications,
            _ => false,
        };
        if (!valid)
            throw new ArgumentException(
                $"Authoritative basis {basis} cannot use metric unit {unit}.",
                nameof(unit)
            );

        Kind = kind;
        NativeAttributeKey = nativeAttributeKey;
        Value = value;
        Unit = unit;
        Basis = basis;
        CanReconcileApplicationCount = canReconcileApplicationCount;
    }

    internal CombatImpactKind Kind { get; }

    internal string NativeAttributeKey { get; }

    internal int Value { get; }

    internal CombatImpactValueUnit Unit { get; }

    internal CombatImpactAuthoritativeBasis Basis { get; }

    internal bool CanReconcileApplicationCount { get; }
}

internal sealed record CombatImpactGroup(
    CombatImpactKind Kind,
    string NativeAttributeKey,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage,
    CombatImpactAuthoritativeMetric? AuthoritativeMetric,
    int UnresolvedTargetCount,
    IReadOnlyList<CombatImpactTarget> Targets
)
{
    internal CombatImpactEventSurface Surface { get; init; } =
        CombatImpactEventSurface.AppliedEffect;

    internal CombatImpactOccurrenceBasis OccurrenceBasis { get; init; } =
        CombatImpactOccurrenceBasis.ReconstructedTransition;

    internal int CriticalCount { get; init; }

    /// <summary>Number of applications whose critical/non-critical outcome is known.</summary>
    internal int CriticalOutcomeCount { get; init; }

    internal int? CriticalObservedValue { get; init; }

    internal bool HasMixedValueDirections { get; init; }

    internal IReadOnlyList<CombatImpactTriggerSource> TriggerSources { get; init; } = [];

    internal int UnattributedTriggerApplicationCount { get; init; }

    internal int TriggerFallbackApplicationCount { get; init; }

    internal int NoTriggerEvidenceApplicationCount { get; init; }

    internal int NotApplicableTriggerApplicationCount { get; init; }

    internal CombatImpactTriggerPresentationState TriggerPresentationState { get; init; }

    internal CombatImpactApplicationLedger ApplicationLedger { get; init; } =
        new(0, null, false, null, CombatImpactControlStatus.NotComparable);

    internal CombatImpactAmountLedger AmountLedger { get; init; } =
        new(
            null,
            null,
            CombatImpactCoverage.None,
            0,
            0,
            CombatImpactValueUnit.Amount,
            CombatImpactValueBasis.None,
            false,
            null,
            CombatImpactResidualCoverage.Unknown,
            CombatImpactControlStatus.NotComparable
        );

    internal CombatImpactPeriodicImpact? PeriodicImpact { get; init; }

    internal bool HasUnattributedTransitionValue { get; init; }

    internal bool HasDivergentTargetCoverage =>
        UnresolvedTargetCount > 0
        || AuthoritativeMetric is { Basis: CombatImpactAuthoritativeBasis.TotalAmount } total
            && (!ObservedValue.HasValue || Unit != total.Unit || ObservedValue.Value != total.Value)
        || AuthoritativeMetric
            is { Basis: CombatImpactAuthoritativeBasis.ApplicationCount } applications
            && Count != applications.Value;
}

internal sealed record CombatImpactTriggerSource(
    CombatImpactEntity Entity,
    int ApplicationCount,
    int ObservedActivationBatchCount
);

internal sealed record CombatImpactSource(
    CombatImpactEntity Entity,
    int UseCount,
    int EffectCount,
    IReadOnlyList<CombatImpactGroup> Groups
)
{
    internal int TotalCount => EffectCount;

    internal int ObservedActivationBatchCount { get; init; }
}

internal sealed record CombatImpactIncomingSource(
    CombatImpactEntity Entity,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage
)
{
    internal int ValuedApplicationCount { get; init; }

    internal bool HasUnattributedTransitionValue { get; init; }
}

internal sealed record CombatImpactIncomingTransitionLedger(
    int NetValue,
    int AttributedValue,
    int ResidualValue,
    int ResidualApplicationCount,
    int ResidualFrameCount,
    CombatImpactValueUnit Unit
);

internal sealed record CombatImpactAttributeTransitionDiagnostic(
    int FrameIndex,
    string TargetId,
    string NativeAttributeKey,
    int NetValue,
    int AttributedValue,
    int ResidualValue,
    int ClaimantCount,
    int UnresolvedClaimantCount,
    CombatImpactValueUnit Unit,
    CombatImpactAttributeTransitionResolution Resolution,
    IReadOnlyList<CombatImpactAttributeTransitionFailureReason> FailureReasons,
    bool EventOrderReplayReconciles,
    bool EventOrderReplayIncludesMultiply
);

internal sealed record CombatImpactIncomingGroup(
    CombatImpactKind Kind,
    string NativeAttributeKey,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage,
    IReadOnlyList<CombatImpactIncomingSource> Sources
)
{
    internal CombatImpactEventSurface Surface { get; init; } =
        CombatImpactEventSurface.AppliedEffect;

    internal CombatImpactOccurrenceBasis OccurrenceBasis { get; init; } =
        CombatImpactOccurrenceBasis.ReconstructedTransition;

    internal int CriticalCount { get; init; }

    /// <summary>Number of applications whose critical/non-critical outcome is known.</summary>
    internal int CriticalOutcomeCount { get; init; }

    internal int? CriticalObservedValue { get; init; }

    internal bool HasMixedValueDirections { get; init; }

    internal int UnresolvedSourceCount { get; init; }

    internal int ValuedApplicationCount { get; init; }

    internal CombatImpactIncomingTransitionLedger? TransitionLedger { get; init; }
}

internal sealed record CombatImpactReceived(
    CombatImpactEntity Entity,
    int EffectCount,
    IReadOnlyList<CombatImpactIncomingGroup> Groups
)
{
    internal int TotalCount => EffectCount;
}

internal sealed record CombatImpactReport(
    IReadOnlyList<CombatImpactSource> Sources,
    IReadOnlyList<CombatImpactReceived> Received
)
{
    internal IReadOnlyList<PeriodicAttributionGap> PeriodicResiduals { get; init; } = [];

    internal IReadOnlyList<CombatImpactProjectionDiagnostic> ProjectionDiagnostics { get; init; } =
    [];

    internal IReadOnlyList<CombatImpactAttributeTransitionDiagnostic> AttributeTransitionDiagnostics { get; init; } =
    [];

    internal CombatImpactCriticalTriggerEvidenceAudit CriticalTriggerEvidenceAudit { get; init; } =
        new(0, 0);

    internal static readonly CombatImpactReport Empty = new(
        Array.Empty<CombatImpactSource>(),
        Array.Empty<CombatImpactReceived>()
    );
}

internal sealed record CombatImpactCriticalTriggerEvidenceAudit(
    int ResolvedOriginCount,
    int AttributedOriginCount
);

internal sealed record CombatImpactProjectionInput(
    IReadOnlyDictionary<string, CombatImpactEntity> Entities,
    IReadOnlyList<CombatImpactEvent> Events,
    IReadOnlyDictionary<string, int> UseCounts,
    IReadOnlyDictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>> AuthoritativeMetrics
)
{
    internal IReadOnlyList<CombatImpactAttributeTransitionResidual> AttributeTransitionResiduals { get; init; } =
    [];
}
