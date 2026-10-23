#nullable enable

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

public enum CombatImpactKind
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

public enum CombatImpactValueUnit
{
    Amount,
    Milliseconds,
    PercentagePoints,
    Applications,
}

public enum CombatImpactEventSurface
{
    AppliedEffect,
    CardAttribute,
    PlayerAttribute,
}

public enum CombatImpactValueBasis
{
    None,
    ExactAdjustment,
    ConfiguredActionAmount,
    NetFrameDelta,
}

public enum CombatImpactOccurrenceBasis
{
    ReconstructedTransition,
    ExplicitExecution,
}

public enum CombatImpactActivitySourceResolution
{
    Direct,
    TriggerFallback,
    PrerequisiteSkill,
}

public enum CombatImpactTriggerScope
{
    AttributedExternal,
    AttributedSelf,
    AttributedViaTriggerFallback,
    NoTriggerEvidence,
    Unattributed,
    NotApplicable,
}

public enum CombatImpactTriggerPresentationState
{
    None,
    Complete,
    PartialBreakdown,
    HiddenSelfOnly,
    BreakdownUnavailable,
}

public enum CombatImpactCoverage
{
    None,
    Exact,
    Estimated,
    LowerBound,
    Partial,
}

public enum CombatImpactAuthoritativeBasis
{
    TotalAmount,
    ApplicationCount,
}

public enum CombatImpactPeriodicKind
{
    Burn,
    Poison,
    Regen,
}

public enum CombatImpactPeriodicProof
{
    Exact,
    Proportional,
}

public enum CombatImpactControlStatus
{
    Exact,
    PositiveResidual,
    OverObserved,
    NotComparable,
}

public enum CombatImpactResidualCoverage
{
    Unknown,
    Exact,
    UpperBound,
}

public enum CombatImpactAttributeTransitionResidualReason
{
    ConcurrentAttributionUnavailable,
    AuraOverlap,
}

public enum CombatImpactAttributeTransitionResolution
{
    SingleClaimantNet,
    ConcurrentConfiguredExact,
    ConcurrentSingleUnknownSolved,
    ConcurrentResidual,
    AuraOverlap,
}

public enum CombatImpactAttributeTransitionFailureReason
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
    AuraOverlap,
}

public sealed record CombatImpactPeriodicImpact(
    int HealthAmount,
    int ShieldAmount,
    CombatImpactPeriodicProof Proof,
    string ModelVersion
);

public enum CombatImpactProjectionDiagnosticKind
{
    MissingPrerequisiteSkill,
    AmbiguousPrerequisiteSkill,
    MissingUseCount,
    ExplicitApplicationsExceedUseCount,
    RuleExecutionMismatch,
    NonDisplayableAuthoritativeMetric,
}

public sealed record CombatImpactProjectionDiagnostic(
    CombatImpactProjectionDiagnosticKind Kind,
    string SourceId,
    string EffectId,
    string? TriggerSourceId = null
);

/// <summary>
/// What the ledger knows about a source or target: identity, display name, kind, and board order.
/// The producer derives a host record that also carries the game-typed facts that attribution and
/// the native preview read; the ledger only passes the instance through.
/// </summary>
public record CombatImpactLedgerEntity(string Id, string Name, string TypeLabel, int Order);

public sealed record CombatImpactEvent(
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
    public CombatImpactEventSurface Surface { get; init; } = CombatImpactEventSurface.AppliedEffect;

    public CombatImpactOccurrenceBasis OccurrenceBasis { get; init; } =
        CombatImpactOccurrenceBasis.ReconstructedTransition;

    public int CriticalCount { get; init; }

    /// <summary>
    /// Number of known critical/non-critical outcomes represented by this event. Exact aggregate
    /// recovery may store several outcomes on one event when their individual identities are lost.
    /// </summary>
    public int CriticalOutcomeCount { get; init; }

    public int? CriticalValue { get; init; }

    public int? NonCriticalValue { get; init; }

    public int? AlternateNonCriticalValue { get; init; }

    public bool HasCriticalAdjustmentCandidate { get; init; }

    public bool IsCritCapable { get; init; }

    public string? RawDirectSourceId { get; init; }

    public string? TriggerSourceId { get; init; }

    public int? TriggerFrameIndex { get; init; }

    public string? EffectId { get; init; }

    public string? ExecutionContextId { get; init; }

    public CombatImpactActivitySourceResolution ActivitySourceResolution { get; init; } =
        CombatImpactActivitySourceResolution.Direct;

    public CombatImpactTriggerScope TriggerScope { get; init; } =
        CombatImpactTriggerScope.NotApplicable;

    public bool IsUnattributedTransitionClaimant { get; init; }

    public int? UnattributedTransitionValue { get; init; }

    public int? AttributeTransitionNetValue { get; init; }

    public CombatImpactAttributeTransitionResolution? AttributeTransitionResolution { get; init; }

    public IReadOnlyList<CombatImpactAttributeTransitionFailureReason> AttributeTransitionFailureReasons { get; init; } =
    [];

    public int AttributeTransitionUnresolvedClaimantCount { get; init; }

    public bool AttributeTransitionEventOrderReplayReconciles { get; init; }

    public bool AttributeTransitionEventOrderReplayIncludesMultiply { get; init; }
}

public sealed record CombatImpactAttributeTransitionResidual(
    int FrameIndex,
    string TargetId,
    string NativeAttributeKey,
    int Value,
    CombatImpactValueUnit Unit,
    int ApplicationCount,
    CombatImpactAttributeTransitionResidualReason Reason
)
{
    public CombatImpactKind Kind => CombatImpactKind.AttributeChange;

    public CombatImpactEventSurface Surface { get; init; } = CombatImpactEventSurface.CardAttribute;
}

public sealed record CombatImpactApplicationLedger(
    int ProjectedApplicationCount,
    int? AuthoritativeApplicationCount,
    bool ComparableToAuthoritativeCount,
    int? ApplicationResidual,
    CombatImpactControlStatus ControlStatus
);

public sealed record CombatImpactAmountLedger(
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

public sealed record CombatImpactTarget(
    CombatImpactLedgerEntity Entity,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage
)
{
    public int ValuedApplicationCount { get; init; }

    public bool HasUnattributedTransitionValue { get; init; }
}

public sealed record CombatImpactAuthoritativeMetric
{
    public CombatImpactAuthoritativeMetric(
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

    public CombatImpactKind Kind { get; }

    public string NativeAttributeKey { get; }

    public int Value { get; }

    public CombatImpactValueUnit Unit { get; }

    public CombatImpactAuthoritativeBasis Basis { get; }

    public bool CanReconcileApplicationCount { get; }
}

public sealed record CombatImpactGroup(
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
    public CombatImpactEventSurface Surface { get; init; } = CombatImpactEventSurface.AppliedEffect;

    public CombatImpactOccurrenceBasis OccurrenceBasis { get; init; } =
        CombatImpactOccurrenceBasis.ReconstructedTransition;

    public int CriticalCount { get; init; }

    /// <summary>Number of applications whose critical/non-critical outcome is known.</summary>
    public int CriticalOutcomeCount { get; init; }

    public int? CriticalObservedValue { get; init; }

    public bool HasMixedValueDirections { get; init; }

    public IReadOnlyList<CombatImpactTriggerSource> TriggerSources { get; init; } = [];

    public int UnattributedTriggerApplicationCount { get; init; }

    public int TriggerFallbackApplicationCount { get; init; }

    public int NoTriggerEvidenceApplicationCount { get; init; }

    public int NotApplicableTriggerApplicationCount { get; init; }

    public CombatImpactTriggerPresentationState TriggerPresentationState { get; init; }

    public CombatImpactApplicationLedger ApplicationLedger { get; init; } =
        new(0, null, false, null, CombatImpactControlStatus.NotComparable);

    public CombatImpactAmountLedger AmountLedger { get; init; } =
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

    public CombatImpactPeriodicImpact? PeriodicImpact { get; init; }

    public bool HasUnattributedTransitionValue { get; init; }
}

public sealed record CombatImpactTriggerSource(
    CombatImpactLedgerEntity Entity,
    int ApplicationCount,
    int ObservedActivationBatchCount
);

public sealed record CombatImpactSource(
    CombatImpactLedgerEntity Entity,
    int UseCount,
    int EffectCount,
    IReadOnlyList<CombatImpactGroup> Groups
)
{
    public int TotalCount => EffectCount;

    public int ObservedActivationBatchCount { get; init; }
}

public sealed record CombatImpactIncomingSource(
    CombatImpactLedgerEntity Entity,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage
)
{
    public int ValuedApplicationCount { get; init; }

    public bool HasUnattributedTransitionValue { get; init; }
}

public sealed record CombatImpactIncomingTransitionLedger(
    int NetValue,
    int AttributedValue,
    int ResidualValue,
    int ResidualApplicationCount,
    int ResidualFrameCount,
    CombatImpactValueUnit Unit
);

public sealed record CombatImpactAttributeTransitionDiagnostic(
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

public sealed record CombatImpactIncomingGroup(
    CombatImpactKind Kind,
    string NativeAttributeKey,
    int Count,
    int? ObservedValue,
    CombatImpactValueUnit Unit,
    CombatImpactCoverage ObservedCoverage,
    IReadOnlyList<CombatImpactIncomingSource> Sources
)
{
    public CombatImpactEventSurface Surface { get; init; } = CombatImpactEventSurface.AppliedEffect;

    public CombatImpactOccurrenceBasis OccurrenceBasis { get; init; } =
        CombatImpactOccurrenceBasis.ReconstructedTransition;

    public int CriticalCount { get; init; }

    /// <summary>Number of applications whose critical/non-critical outcome is known.</summary>
    public int CriticalOutcomeCount { get; init; }

    public int? CriticalObservedValue { get; init; }

    public bool HasMixedValueDirections { get; init; }

    public int UnresolvedSourceCount { get; init; }

    public int ValuedApplicationCount { get; init; }

    public CombatImpactIncomingTransitionLedger? TransitionLedger { get; init; }
}

public sealed record CombatImpactReceived(
    CombatImpactLedgerEntity Entity,
    int EffectCount,
    IReadOnlyList<CombatImpactIncomingGroup> Groups
)
{
    public int TotalCount => EffectCount;
}

public sealed record CombatImpactReport(
    IReadOnlyList<CombatImpactSource> Sources,
    IReadOnlyList<CombatImpactReceived> Received
)
{
    public IReadOnlyList<CombatImpactProjectionDiagnostic> ProjectionDiagnostics { get; init; } =
    [];

    public IReadOnlyList<CombatImpactAttributeTransitionDiagnostic> AttributeTransitionDiagnostics { get; init; } =
    [];

    public CombatImpactCriticalTriggerEvidenceAudit CriticalTriggerEvidenceAudit { get; init; } =
        new(0, 0);

    public static readonly CombatImpactReport Empty = new(
        Array.Empty<CombatImpactSource>(),
        Array.Empty<CombatImpactReceived>()
    );
}

public sealed record CombatImpactCriticalTriggerEvidenceAudit(
    int ResolvedOriginCount,
    int AttributedOriginCount
)
{
    public int InputEventCount { get; init; }

    public int InputExecutionCount { get; init; }

    public int ObservedEvidenceCount { get; init; }

    public long WorkUnitCount { get; init; }
}

public sealed record CombatImpactProjectionInput(
    IReadOnlyDictionary<string, CombatImpactLedgerEntity> Entities,
    IReadOnlyList<CombatImpactEvent> Events,
    IReadOnlyDictionary<string, int> UseCounts,
    IReadOnlyDictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>> AuthoritativeMetrics
)
{
    public IReadOnlyList<CombatImpactAttributeTransitionResidual> AttributeTransitionResiduals { get; init; } =
    [];
}
