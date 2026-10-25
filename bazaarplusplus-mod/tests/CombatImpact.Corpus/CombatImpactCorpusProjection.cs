#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Core.Combat;
using BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactCorpusProjection
{
    internal static CombatImpactCorpusProjectionResult Analyze(
        IReadOnlyList<CombatImpactCorpusBattle> replays,
        bool benchmarkEnabled
    )
    {
        var orderedInputs = replays
            .OrderBy(replay => replay.BattleId, StringComparer.Ordinal)
            .ToArray();
        // The first projection warms the JIT so the benchmark times the projection alone.
        if (benchmarkEnabled && orderedInputs.Length > 0)
            CombatImpactProjector.Project(orderedInputs[0].Timeline, orderedInputs[0].Entities);

        var benchmarkObservations = benchmarkEnabled
            ? new List<CombatImpactProjectionBenchmarkObservation>(replays.Count)
            : null;
        var diagnostics = new List<CardAttributeAttributionCorpusObservation>();
        var rawExecutions = 0;
        var implicitPlayerEffects = 0;
        var criticalTriggerResolvedOrigins = 0;
        var criticalTriggerAttributedOrigins = 0;
        var criticalTriggerEvidenceFailures = new List<string>();
        foreach (var replay in orderedInputs)
        {
            rawExecutions += replay.Timeline.Frames.Sum(frame =>
                frame
                    .Events.OfType<CombatEffectExecuted>()
                    .Count(effect =>
                        effect.ActionType == CombatActionType.CardModifyAttribute
                        && effect.Target is CombatCardTarget
                    )
            );
            var entities = replay.Entities;
            implicitPlayerEffects += replay.ImplicitPlayerEffects;
            var allocatedBefore = benchmarkEnabled ? GC.GetAllocatedBytesForCurrentThread() : 0;
            var startedAt = benchmarkEnabled ? Stopwatch.GetTimestamp() : 0;
            var report = CombatImpactProjector.Project(replay.Timeline, entities);
            if (benchmarkObservations != null)
            {
                var elapsed = Stopwatch.GetElapsedTime(startedAt);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                benchmarkObservations.Add(
                    CombatImpactProjectionBenchmark.Observe(replay, report, elapsed, allocated)
                );
            }
            criticalTriggerResolvedOrigins += report
                .CriticalTriggerEvidenceAudit
                .ResolvedOriginCount;
            criticalTriggerAttributedOrigins += report
                .CriticalTriggerEvidenceAudit
                .AttributedOriginCount;
            if (
                report.CriticalTriggerEvidenceAudit.AttributedOriginCount
                != report.CriticalTriggerEvidenceAudit.ResolvedOriginCount
            )
                criticalTriggerEvidenceFailures.Add(
                    $"{replay.BattleId}:"
                        + $"resolved={report.CriticalTriggerEvidenceAudit.ResolvedOriginCount}/"
                        + $"attributed={report.CriticalTriggerEvidenceAudit.AttributedOriginCount}"
                );
            diagnostics.AddRange(
                report.AttributeTransitionDiagnostics.Select(item =>
                    Observe(replay, item, entities)
                )
            );
        }

        var failures = diagnostics
            .Where(item =>
                item.Resolution
                == CombatImpactAttributeTransitionResolution.ConcurrentResidual.ToString()
            )
            .SelectMany(item => item.FailureReasons.Distinct(StringComparer.Ordinal))
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var solvedReasons = diagnostics
            .Where(item =>
                item.Resolution
                == CombatImpactAttributeTransitionResolution.ConcurrentSingleUnknownSolved.ToString()
            )
            .SelectMany(item => item.FailureReasons.Distinct(StringComparer.Ordinal))
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var eventOrderReplayReasons = diagnostics
            .Where(item => item.EventOrderReplayReconciles)
            .SelectMany(item => item.FailureReasons.Distinct(StringComparer.Ordinal))
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var conservationFailures = diagnostics
            .Where(item => item.AttributedValue + item.ResidualValue != item.NetValue)
            .ToArray();
        if (criticalTriggerAttributedOrigins != criticalTriggerResolvedOrigins)
            throw new InvalidOperationException(
                "Resolved on-card-critted trigger evidence was not fully attributed: "
                    + $"resolved={criticalTriggerResolvedOrigins} "
                    + $"attributed={criticalTriggerAttributedOrigins} "
                    + $"battles={string.Join(',', criticalTriggerEvidenceFailures.Take(50))}."
            );
        var attribution = new CardAttributeAttributionCorpusReport(
            replays.Count,
            rawExecutions,
            implicitPlayerEffects,
            diagnostics.Count,
            diagnostics.Sum(item => item.ClaimantCount),
            diagnostics.Count(item =>
                item.Resolution == CombatImpactAttributeTransitionResolution.AuraOverlap.ToString()
            ),
            diagnostics.Count(item =>
                item.Resolution
                == CombatImpactAttributeTransitionResolution.SingleClaimantNet.ToString()
            ),
            diagnostics.Count(item =>
                item.Resolution
                == CombatImpactAttributeTransitionResolution.ConcurrentConfiguredExact.ToString()
            ),
            diagnostics.Count(item =>
                item.Resolution
                == CombatImpactAttributeTransitionResolution.ConcurrentSingleUnknownSolved.ToString()
            ),
            diagnostics.Count(item =>
                item.Resolution
                == CombatImpactAttributeTransitionResolution.ConcurrentResidual.ToString()
            ),
            diagnostics.Count(item =>
                item.Resolution
                    == CombatImpactAttributeTransitionResolution.ConcurrentResidual.ToString()
                && item.UnresolvedClaimantCount == 1
            ),
            diagnostics.Count(item =>
                item.Resolution
                    == CombatImpactAttributeTransitionResolution.ConcurrentResidual.ToString()
                && item.UnresolvedClaimantCount == 1
                && !item.FailureReasons.Contains(
                    CombatImpactAttributeTransitionFailureReason.ModifierUnavailable.ToString(),
                    StringComparer.Ordinal
                )
            ),
            diagnostics.Count(item =>
                item.Resolution
                    == CombatImpactAttributeTransitionResolution.ConcurrentResidual.ToString()
                && item.UnresolvedClaimantCount == 1
                && item.FailureReasons.Contains(
                    CombatImpactAttributeTransitionFailureReason.RangeValue.ToString(),
                    StringComparer.Ordinal
                )
            ),
            diagnostics.Count(item => item.EventOrderReplayReconciles),
            diagnostics.Count(item =>
                item.EventOrderReplayReconciles && item.EventOrderReplayIncludesMultiply
            ),
            conservationFailures.Length,
            failures,
            solvedReasons,
            eventOrderReplayReasons,
            diagnostics
                .Where(item =>
                    item.Resolution
                        == CombatImpactAttributeTransitionResolution.ConcurrentResidual.ToString()
                    || item.Resolution
                        == CombatImpactAttributeTransitionResolution.AuraOverlap.ToString()
                )
                .ToArray()
        );
        return new CombatImpactCorpusProjectionResult(
            attribution,
            benchmarkObservations == null
                ? null
                : CombatImpactProjectionBenchmark.Summarize(benchmarkObservations)
        );
    }

    private static CardAttributeAttributionCorpusObservation Observe(
        CombatImpactCorpusBattle replay,
        CombatImpactAttributeTransitionDiagnostic item,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        new(
            replay.BattleId,
            item.FrameIndex,
            item.TargetId,
            item.NativeAttributeKey,
            item.NetValue,
            item.AttributedValue,
            item.ResidualValue,
            item.ClaimantCount,
            item.UnresolvedClaimantCount,
            item.Resolution.ToString(),
            item.FailureReasons.Select(reason => reason.ToString()).ToArray(),
            item.EventOrderReplayReconciles,
            item.EventOrderReplayIncludesMultiply,
            item.Resolution == CombatImpactAttributeTransitionResolution.ConcurrentResidual
                ? ReadClaimants(replay, item, entities)
                : []
        );

    private static IReadOnlyList<CardAttributeClaimantCorpusObservation> ReadClaimants(
        CombatImpactCorpusBattle replay,
        CombatImpactAttributeTransitionDiagnostic diagnostic,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (diagnostic.FrameIndex < 0 || diagnostic.FrameIndex >= replay.Timeline.Frames.Count)
            return [];

        return replay
            .Timeline.Frames[diagnostic.FrameIndex]
            .Events.OfType<CombatEffectExecuted>()
            .Where(effect =>
                effect.ActionType == CombatActionType.CardModifyAttribute
                && effect.Target is CombatCardTarget target
                && string.Equals(target.Target, diagnostic.TargetId, StringComparison.Ordinal)
            )
            .Select(effect => ReadClaimant(effect, entities))
            .ToArray();
    }

    private static CardAttributeClaimantCorpusObservation ReadClaimant(
        CombatEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var directSourceId = effect.Source;
        var triggerSourceId = effect.TriggerSource;
        var modifierSourceId = ResolveModifierSource(
            directSourceId,
            triggerSourceId,
            effect.EffectId,
            entities,
            out var modifier
        );
        return new CardAttributeClaimantCorpusObservation(
            directSourceId,
            directSourceId != null && entities.TryGetValue(directSourceId, out var direct)
                ? direct.Name
                : null,
            triggerSourceId,
            triggerSourceId != null && entities.TryGetValue(triggerSourceId, out var trigger)
                ? trigger.Name
                : null,
            effect.EffectId,
            modifierSourceId,
            modifier?.AttributeType.ToString(),
            modifier?.Operation.ToString(),
            GameValueTypeName(modifier?.Value)
        );
    }

    // The evidence names a modifier's value by the game's value type, as it did before the
    // producer moved to Core's combat vocabulary (ADR-0019). The names are fixed here because the
    // Core lane has no game types to read them from; the host adapter's DesktopCombatTimeline.ToValue
    // owns which game type becomes which Core value.
    private static string? GameValueTypeName(CombatValue? value) =>
        value switch
        {
            null => null,
            CombatFixedValue => "TFixedValue",
            CombatRangeValue => "TRangeValue",
            CombatCardAttributeReference => "TReferenceValueCardAttribute",
            CombatOpaqueReference { Kind: CombatOpaqueReferenceKind.CardAttributeUnscaled } =>
                "TReferenceValueCardAttributeUnscaled",
            CombatOpaqueReference { Kind: CombatOpaqueReferenceKind.AttributeChange } =>
                "TReferenceValueAttributeChange",
            CombatOpaqueReference { Kind: CombatOpaqueReferenceKind.PlayerAttribute } =>
                "TReferenceValuePlayerAttribute",
            CombatOpaqueReference { Kind: CombatOpaqueReferenceKind.CardCount } =>
                "TReferenceValueCardCount",
            _ => value.GetType().Name,
        };

    private static string? ResolveModifierSource(
        string? directSourceId,
        string? triggerSourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out CombatAttributeModifier? modifier
    )
    {
        foreach (var candidate in new[] { directSourceId, triggerSourceId })
        {
            if (
                !string.IsNullOrWhiteSpace(candidate)
                && !string.IsNullOrWhiteSpace(effectId)
                && entities.TryGetValue(candidate!, out var entity)
                && entity.AbilityAttributeModifiersByEffectId?.TryGetValue(effectId!, out modifier)
                    == true
            )
            {
                return candidate;
            }
        }

        modifier = null;
        return null;
    }
}

internal sealed record CombatImpactCorpusProjectionResult(
    CardAttributeAttributionCorpusReport Attribution,
    CombatImpactProjectionBenchmarkReport? Benchmark
);

internal sealed record CardAttributeAttributionCorpusReport(
    int Battles,
    int RawTargetedExecutions,
    int ImplicitPlayerEffects,
    int DiagnosticGroups,
    int DiagnosedClaimants,
    int AuraOverlapGroups,
    int SingleClaimantGroups,
    int ConcurrentExactGroups,
    int SingleUnknownSolvedGroups,
    int ResidualGroups,
    int SingleUnknownResidualGroups,
    int RemainingSingleUnknownSolvableGroups,
    int SingleUnknownRangeGroups,
    int EventOrderReplayCandidateGroups,
    int EventOrderReplayMultiplyGroups,
    int ConservationFailures,
    IReadOnlyDictionary<string, int> FailureReasons,
    IReadOnlyDictionary<string, int> SingleUnknownSolvedReasons,
    IReadOnlyDictionary<string, int> EventOrderReplayCandidateReasons,
    IReadOnlyList<CardAttributeAttributionCorpusObservation> ResidualObservations
);

internal sealed record CardAttributeAttributionCorpusObservation(
    string BattleId,
    int FrameIndex,
    string TargetId,
    string NativeAttributeKey,
    int NetValue,
    int AttributedValue,
    int ResidualValue,
    int ClaimantCount,
    int UnresolvedClaimantCount,
    string Resolution,
    IReadOnlyList<string> FailureReasons,
    bool EventOrderReplayReconciles,
    bool EventOrderReplayIncludesMultiply,
    IReadOnlyList<CardAttributeClaimantCorpusObservation> Claimants
);

internal sealed record CardAttributeClaimantCorpusObservation(
    string? DirectSourceId,
    string? DirectSourceName,
    string? TriggerSourceId,
    string? TriggerSourceName,
    string? EffectId,
    string? ModifierSourceId,
    string? ModifierAttribute,
    string? ModifierOperation,
    string? ModifierValueType
);
