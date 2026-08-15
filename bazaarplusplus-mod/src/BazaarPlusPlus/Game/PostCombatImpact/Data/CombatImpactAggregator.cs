#nullable enable

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactAggregator
{
    internal static CombatImpactReport Aggregate(CombatImpactProjectionInput input)
    {
        var sourceIds = new HashSet<string>(
            input.Events.Select(item => item.SourceId),
            StringComparer.Ordinal
        );
        sourceIds.UnionWith(input.AuthoritativeMetrics.Keys);

        var sources = sourceIds
            .Select(sourceId => BuildSource(input, sourceId))
            .Where(source => source != null)
            .Cast<CombatImpactSource>()
            .OrderByDescending(source => source.TotalCount)
            .ThenBy(source => source.Entity.Order)
            .ThenBy(source => source.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Entity.Id, StringComparer.Ordinal)
            .ToArray();

        var receivedTargetIds = new HashSet<string>(
            input.Events.Select(item => item.TargetId),
            StringComparer.Ordinal
        );
        receivedTargetIds.UnionWith(
            input.AttributeTransitionResiduals.Select(item => item.TargetId)
        );
        var received = receivedTargetIds
            .Select(targetId => BuildReceived(input, targetId))
            .Where(target => target != null)
            .Cast<CombatImpactReceived>()
            .OrderByDescending(target => target.TotalCount)
            .ThenBy(target => target.Entity.Order)
            .ThenBy(target => target.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Entity.Id, StringComparer.Ordinal)
            .ToArray();

        return new CombatImpactReport(sources, received);
    }

    private static CombatImpactSource? BuildSource(
        CombatImpactProjectionInput input,
        string sourceId
    )
    {
        if (!input.Entities.TryGetValue(sourceId, out var sourceEntity))
            return null;
        if (sourceEntity.TypeLabel is not ("Item" or "Skill"))
            return null;

        var events = input
            .Events.Where(item => string.Equals(item.SourceId, sourceId, StringComparison.Ordinal))
            .ToArray();
        var authoritativeMetrics =
            input.AuthoritativeMetrics.GetValueOrDefault(sourceId)
            ?? Array.Empty<CombatImpactAuthoritativeMetric>();
        var groupKeys = new HashSet<GroupKey>(
            events.Select(item => Key(item.Kind, item.NativeAttributeKey, item.Surface))
        );
        groupKeys.UnionWith(
            authoritativeMetrics.Select(metric => new GroupKey(
                metric.Kind,
                metric.NativeAttributeKey,
                CombatImpactEventSurface.AppliedEffect
            ))
        );

        var groups = groupKeys
            .Select(key =>
                BuildGroup(
                    input.Entities,
                    key,
                    events
                        .Where(item => Key(item.Kind, item.NativeAttributeKey, item.Surface) == key)
                        .ToArray(),
                    authoritativeMetrics.FirstOrDefault(metric =>
                        key.Surface == CombatImpactEventSurface.AppliedEffect
                        && metric.Kind == key.Kind
                        && string.Equals(
                            metric.NativeAttributeKey,
                            key.NativeAttributeKey,
                            StringComparison.Ordinal
                        )
                    )
                )
            )
            .ToList();
        var orderedGroups = groups
            .Where(group => group.Count > 0 || group.AuthoritativeMetric != null)
            .OrderBy(group => GroupOrder(group.Kind))
            .ThenBy(group => group.NativeAttributeKey, StringComparer.Ordinal)
            .ThenBy(group => group.Surface)
            .ToArray();
        if (orderedGroups.Length == 0)
            return null;

        return new CombatImpactSource(
            sourceEntity,
            input.UseCounts.GetValueOrDefault(sourceId),
            events.Length,
            orderedGroups
        )
        {
            ObservedActivationBatchCount = BuildObservedActivationBatchCount(events),
        };
    }

    private static CombatImpactGroup BuildGroup(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        GroupKey key,
        IReadOnlyList<CombatImpactEvent> events,
        CombatImpactAuthoritativeMetric? authoritativeMetric
    )
    {
        var targets = events
            .GroupBy(item => item.TargetId, StringComparer.Ordinal)
            .Select(group => BuildTarget(entities, group.ToArray()))
            .Where(target => target != null)
            .Cast<CombatImpactTarget>()
            .OrderByDescending(target => target.Count)
            .ThenBy(target => target.Entity.Order)
            .ThenBy(target => target.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Entity.Id, StringComparer.Ordinal)
            .ToArray();
        var unresolvedTargetCount = Math.Max(0, events.Count - targets.Sum(target => target.Count));

        var observed = BuildObserved(events);
        var critical = BuildCritical(events);
        var hasMixedValueDirections = HasMixedValueDirections(events);
        var triggerLedger = BuildTriggerLedger(entities, events);
        return new CombatImpactGroup(
            key.Kind,
            key.NativeAttributeKey,
            events.Count,
            observed.Value,
            observed.Value.HasValue
                ? observed.Unit
                : events.FirstOrDefault()?.Unit
                    ?? authoritativeMetric?.Unit
                    ?? CombatImpactValueUnit.Amount,
            observed.Coverage,
            authoritativeMetric,
            unresolvedTargetCount,
            targets
        )
        {
            Surface = key.Surface,
            OccurrenceBasis = BuildOccurrenceBasis(events),
            CriticalCount = critical.Count,
            CriticalOutcomeCount = critical.OutcomeCount,
            CriticalObservedValue = critical.Observed.Value,
            HasMixedValueDirections = hasMixedValueDirections,
            TriggerSources = triggerLedger.Sources,
            UnattributedTriggerApplicationCount = triggerLedger.UnattributedCount,
            TriggerFallbackApplicationCount = triggerLedger.FallbackCount,
            NoTriggerEvidenceApplicationCount = triggerLedger.NoEvidenceCount,
            NotApplicableTriggerApplicationCount = triggerLedger.NotApplicableCount,
            TriggerPresentationState = triggerLedger.PresentationState,
            ApplicationLedger = BuildApplicationLedger(events.Count, authoritativeMetric),
            AmountLedger = BuildAmountLedger(
                events,
                observed,
                authoritativeMetric,
                hasMixedValueDirections
            ),
            HasUnattributedTransitionValue = events.Any(item =>
                item.IsUnattributedTransitionClaimant
            ),
        };
    }

    private static CombatImpactReceived? BuildReceived(
        CombatImpactProjectionInput input,
        string targetId
    )
    {
        if (!input.Entities.TryGetValue(targetId, out var targetEntity))
            return null;
        if (targetEntity.TypeLabel is not ("Item" or "Skill"))
            return null;

        var events = input
            .Events.Where(item => string.Equals(item.TargetId, targetId, StringComparison.Ordinal))
            .ToArray();
        var residuals = input
            .AttributeTransitionResiduals.Where(item =>
                string.Equals(item.TargetId, targetId, StringComparison.Ordinal)
            )
            .ToArray();
        var groupKeys = new HashSet<GroupKey>(
            events.Select(item => Key(item.Kind, item.NativeAttributeKey, item.Surface))
        );
        groupKeys.UnionWith(
            residuals.Select(item => Key(item.Kind, item.NativeAttributeKey, item.Surface))
        );
        var groups = groupKeys
            .Select(key =>
                BuildIncomingGroup(
                    input.Entities,
                    key,
                    events
                        .Where(item => Key(item.Kind, item.NativeAttributeKey, item.Surface) == key)
                        .ToArray(),
                    residuals
                        .Where(item => Key(item.Kind, item.NativeAttributeKey, item.Surface) == key)
                        .ToArray()
                )
            )
            .ToList();

        var orderedGroups = groups
            .OrderBy(group => GroupOrder(group.Kind))
            .ThenBy(group => group.NativeAttributeKey, StringComparer.Ordinal)
            .ThenBy(group => group.Surface)
            .ToArray();
        if (orderedGroups.Length == 0)
            return null;

        return new CombatImpactReceived(
            targetEntity,
            orderedGroups.Sum(group => group.Count),
            orderedGroups
        );
    }

    private static CombatImpactIncomingGroup BuildIncomingGroup(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        GroupKey key,
        IReadOnlyList<CombatImpactEvent> events,
        IReadOnlyList<CombatImpactAttributeTransitionResidual> residuals
    )
    {
        var sources = events
            .GroupBy(item => item.SourceId, StringComparer.Ordinal)
            .Select(group => BuildIncomingSource(entities, group.ToArray()))
            .Where(source => source != null)
            .Cast<CombatImpactIncomingSource>()
            .OrderByDescending(source => source.Count)
            .ThenBy(source => source.Entity.Order)
            .ThenBy(source => source.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Entity.Id, StringComparer.Ordinal)
            .ToArray();
        var observed = BuildObserved(events);
        var transitionLedger = BuildIncomingTransitionLedger(observed, residuals);
        var count = events.Count > 0 ? events.Count : residuals.Sum(item => item.ApplicationCount);
        var unresolvedSourceCount = Math.Max(0, count - sources.Sum(source => source.Count));

        var critical = BuildCritical(events);
        return new CombatImpactIncomingGroup(
            key.Kind,
            key.NativeAttributeKey,
            count,
            observed.Value,
            transitionLedger?.Unit ?? observed.Unit,
            observed.Coverage,
            sources
        )
        {
            Surface = key.Surface,
            OccurrenceBasis = BuildOccurrenceBasis(events),
            CriticalCount = critical.Count,
            CriticalOutcomeCount = critical.OutcomeCount,
            CriticalObservedValue = critical.Observed.Value,
            HasMixedValueDirections = HasMixedValueDirections(events, residuals),
            UnresolvedSourceCount = unresolvedSourceCount,
            ValuedApplicationCount = observed.ValuedApplicationCount,
            TransitionLedger = transitionLedger,
        };
    }

    private static CombatImpactIncomingTransitionLedger? BuildIncomingTransitionLedger(
        ObservedAggregate observed,
        IReadOnlyList<CombatImpactAttributeTransitionResidual> residuals
    )
    {
        if (residuals.Count == 0)
            return null;

        var units = residuals.Select(item => item.Unit).ToHashSet();
        if (observed.Value.HasValue)
            units.Add(observed.Unit);
        if (units.Count != 1)
            return null;

        var attributedValue = observed.Value.GetValueOrDefault();
        var residualValue = SaturatingSum(residuals.Select(item => item.Value));
        return new CombatImpactIncomingTransitionLedger(
            SaturatingSum([attributedValue, residualValue]),
            attributedValue,
            residualValue,
            residuals.Sum(item => item.ApplicationCount),
            residuals.Select(item => item.FrameIndex).Distinct().Count(),
            units.Single()
        );
    }

    private static bool HasMixedValueDirections(IReadOnlyList<CombatImpactEvent> events) =>
        events.Any(item => item.Value is > 0) && events.Any(item => item.Value is < 0);

    private static bool HasMixedValueDirections(
        IReadOnlyList<CombatImpactEvent> events,
        IReadOnlyList<CombatImpactAttributeTransitionResidual> residuals
    )
    {
        var hasPositive =
            events.Any(item => item.Value is > 0) || residuals.Any(item => item.Value > 0);
        var hasNegative =
            events.Any(item => item.Value is < 0) || residuals.Any(item => item.Value < 0);
        return hasPositive && hasNegative;
    }

    private static CriticalAggregate BuildCritical(IReadOnlyList<CombatImpactEvent> events)
    {
        var criticalCount = events.Sum(item =>
            item.CriticalCount > 0 ? item.CriticalCount
            : item.IsCritical ? 1
            : 0
        );
        var criticalOutcomeCount = events.Sum(item =>
            item.CriticalOutcomeCount > 0 ? item.CriticalOutcomeCount
            : item.IsCritical ? 1
            : 0
        );
        if (criticalCount == 0)
            return new CriticalAggregate(
                0,
                criticalOutcomeCount,
                new ObservedAggregate(
                    null,
                    events.FirstOrDefault()?.Unit ?? CombatImpactValueUnit.Amount,
                    CombatImpactCoverage.None
                )
            );

        var contributing = events
            .Where(item => item.CriticalCount > 0 || item.IsCritical)
            .ToArray();
        var units = contributing.Select(item => item.Unit).Distinct().ToArray();
        var values = contributing
            .Select(item => item.CriticalValue ?? (item.IsCritical ? item.Value : null))
            .ToArray();
        var hasEveryValue = values.All(value => value.HasValue);
        int? value = values.Any(item => item.HasValue)
            ? SaturatingSum(values.Where(item => item.HasValue).Select(item => item!.Value))
            : null;
        return new CriticalAggregate(
            criticalCount,
            criticalOutcomeCount,
            new ObservedAggregate(
                value,
                units.Length == 1 ? units[0] : CombatImpactValueUnit.Amount,
                !value.HasValue ? CombatImpactCoverage.None
                    : hasEveryValue && units.Length == 1 ? CombatImpactCoverage.Exact
                    : CombatImpactCoverage.Partial
            )
        );
    }

    private static CombatImpactIncomingSource? BuildIncomingSource(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyList<CombatImpactEvent> events
    )
    {
        if (!entities.TryGetValue(events[0].SourceId, out var entity))
            return null;

        var observed = BuildObserved(events);
        return new CombatImpactIncomingSource(
            entity,
            events.Count,
            observed.Value,
            observed.Unit,
            observed.Coverage
        )
        {
            ValuedApplicationCount = observed.ValuedApplicationCount,
            HasUnattributedTransitionValue = events.Any(item =>
                item.IsUnattributedTransitionClaimant
            ),
        };
    }

    private static CombatImpactTarget? BuildTarget(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyList<CombatImpactEvent> events
    )
    {
        if (!entities.TryGetValue(events[0].TargetId, out var entity))
            return null;

        var observed = BuildObserved(events);
        return new CombatImpactTarget(
            entity,
            events.Count,
            observed.Value,
            observed.Unit,
            observed.Coverage
        )
        {
            ValuedApplicationCount = observed.ValuedApplicationCount,
            HasUnattributedTransitionValue = events.Any(item =>
                item.IsUnattributedTransitionClaimant
            ),
        };
    }

    private static CombatImpactOccurrenceBasis BuildOccurrenceBasis(
        IReadOnlyList<CombatImpactEvent> events
    ) =>
        events.Count > 0
        && events.All(item => item.OccurrenceBasis == CombatImpactOccurrenceBasis.ExplicitExecution)
            ? CombatImpactOccurrenceBasis.ExplicitExecution
            : CombatImpactOccurrenceBasis.ReconstructedTransition;

    private static ObservedAggregate BuildObserved(IReadOnlyList<CombatImpactEvent> events)
    {
        var fallbackUnit = events.FirstOrDefault()?.Unit ?? CombatImpactValueUnit.Amount;
        var knownValues = events.Where(item => item.Value.HasValue).ToArray();
        if (knownValues.Length == 0)
            return new ObservedAggregate(null, fallbackUnit, CombatImpactCoverage.None);

        var units = knownValues.Select(item => item.Unit).Distinct().ToArray();
        if (units.Length != 1)
            return new ObservedAggregate(
                null,
                fallbackUnit,
                CombatImpactCoverage.Partial,
                knownValues.Length,
                WeakestValueBasis(knownValues),
                AllKnownValuesExact(knownValues)
            );

        var coverage =
            knownValues.Length != events.Count ? CombatImpactCoverage.Partial
            : knownValues.Any(item => item.ValueBasis == CombatImpactValueBasis.None)
                ? CombatImpactCoverage.Partial
            : knownValues.Any(item => item.ValueBasis == CombatImpactValueBasis.NetFrameDelta)
                ? CombatImpactCoverage.LowerBound
            : knownValues.Any(item =>
                item.ValueBasis == CombatImpactValueBasis.ConfiguredActionAmount
            )
                ? CombatImpactCoverage.Estimated
            : CombatImpactCoverage.Exact;
        return new ObservedAggregate(
            SumValues(knownValues),
            units[0],
            coverage,
            knownValues.Length,
            WeakestValueBasis(knownValues),
            AllKnownValuesExact(knownValues)
        );
    }

    private static int BuildObservedActivationBatchCount(IReadOnlyList<CombatImpactEvent> events) =>
        events
            .Where(item =>
                item.TriggerScope
                    is CombatImpactTriggerScope.AttributedExternal
                        or CombatImpactTriggerScope.AttributedSelf
                        or CombatImpactTriggerScope.AttributedViaTriggerFallback
                && !string.IsNullOrWhiteSpace(item.TriggerSourceId)
                && item.TriggerFrameIndex.HasValue
            )
            .Select(item => (item.TriggerSourceId, item.TriggerFrameIndex!.Value))
            .Distinct()
            .Count();

    private static TriggerLedger BuildTriggerLedger(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyList<CombatImpactEvent> events
    )
    {
        var attributable = events
            .Where(item =>
                item.TriggerScope
                    is CombatImpactTriggerScope.AttributedExternal
                        or CombatImpactTriggerScope.AttributedSelf
            )
            .ToArray();
        var sources = attributable
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.TriggerSourceId)
                && entities.ContainsKey(item.TriggerSourceId)
            )
            .GroupBy(item => item.TriggerSourceId!, StringComparer.Ordinal)
            .Select(group => new CombatImpactTriggerSource(
                entities[group.Key],
                group.Count(),
                group
                    .Where(item => item.TriggerFrameIndex.HasValue)
                    .Select(item => item.TriggerFrameIndex!.Value)
                    .Distinct()
                    .Count()
            ))
            .OrderByDescending(source => source.ApplicationCount)
            .ThenBy(source => source.Entity.Order)
            .ThenBy(source => source.Entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Entity.Id, StringComparer.Ordinal)
            .ToArray();
        var representedCount = sources.Sum(source => source.ApplicationCount);
        var unattributedCount =
            events.Count(item => item.TriggerScope == CombatImpactTriggerScope.Unattributed)
            + Math.Max(0, attributable.Length - representedCount);
        var fallbackCount = events.Count(item =>
            item.TriggerScope == CombatImpactTriggerScope.AttributedViaTriggerFallback
        );
        var noEvidenceCount = events.Count(item =>
            item.TriggerScope == CombatImpactTriggerScope.NoTriggerEvidence
        );
        var notApplicableCount = events.Count(item =>
            item.TriggerScope == CombatImpactTriggerScope.NotApplicable
        );
        var remainderCount =
            unattributedCount + fallbackCount + noEvidenceCount + notApplicableCount;
        var hasExternal = events.Any(item =>
            item.TriggerScope == CombatImpactTriggerScope.AttributedExternal
        );
        var presentationState =
            representedCount == 0
                ? unattributedCount > 0 || fallbackCount > 0
                    ? CombatImpactTriggerPresentationState.BreakdownUnavailable
                    : CombatImpactTriggerPresentationState.None
                : !hasExternal && remainderCount == 0
                    ? CombatImpactTriggerPresentationState.HiddenSelfOnly
                    : remainderCount > 0
                        ? CombatImpactTriggerPresentationState.PartialBreakdown
                        : CombatImpactTriggerPresentationState.Complete;

        return new TriggerLedger(
            sources,
            unattributedCount,
            fallbackCount,
            noEvidenceCount,
            notApplicableCount,
            presentationState
        );
    }

    private static CombatImpactApplicationLedger BuildApplicationLedger(
        int projectedCount,
        CombatImpactAuthoritativeMetric? authoritative
    )
    {
        if (authoritative?.Basis != CombatImpactAuthoritativeBasis.ApplicationCount)
            return new CombatImpactApplicationLedger(
                projectedCount,
                null,
                false,
                null,
                CombatImpactControlStatus.NotComparable
            );

        if (!authoritative.CanReconcileApplicationCount)
            return new CombatImpactApplicationLedger(
                projectedCount,
                authoritative.Value,
                false,
                null,
                CombatImpactControlStatus.NotComparable
            );

        var residual = authoritative.Value - projectedCount;
        return residual < 0
            ? new CombatImpactApplicationLedger(
                projectedCount,
                authoritative.Value,
                true,
                null,
                CombatImpactControlStatus.OverObserved
            )
            : new CombatImpactApplicationLedger(
                projectedCount,
                authoritative.Value,
                true,
                residual,
                residual == 0
                    ? CombatImpactControlStatus.Exact
                    : CombatImpactControlStatus.PositiveResidual
            );
    }

    private static CombatImpactAmountLedger BuildAmountLedger(
        IReadOnlyList<CombatImpactEvent> events,
        ObservedAggregate observed,
        CombatImpactAuthoritativeMetric? authoritative,
        bool hasMixedValueDirections
    )
    {
        int? authoritativeTotal =
            authoritative?.Basis == CombatImpactAuthoritativeBasis.TotalAmount
                ? authoritative.Value
                : null;
        var hasComparableObservedBasis =
            observed.Coverage is CombatImpactCoverage.Exact or CombatImpactCoverage.LowerBound
            || observed.Coverage == CombatImpactCoverage.Partial && observed.AllKnownValuesExact;
        var comparable =
            authoritativeTotal.HasValue
            && observed.Value.HasValue
            && authoritative!.Unit == observed.Unit
            && !hasMixedValueDirections
            && observed.Value.GetValueOrDefault() >= 0
            && hasComparableObservedBasis;
        long? residual = null;
        var residualCoverage = CombatImpactResidualCoverage.Unknown;
        var status = CombatImpactControlStatus.NotComparable;
        if (comparable)
        {
            var difference = (long)authoritativeTotal!.Value - observed.Value!.Value;
            if (difference < 0)
            {
                status = CombatImpactControlStatus.OverObserved;
            }
            else
            {
                residual = difference;
                residualCoverage = observed.Coverage switch
                {
                    CombatImpactCoverage.Exact => CombatImpactResidualCoverage.Exact,
                    CombatImpactCoverage.Partial
                        when observed.AllKnownValuesExact
                            && observed.ValuedApplicationCount < events.Count =>
                        CombatImpactResidualCoverage.Exact,
                    CombatImpactCoverage.LowerBound => CombatImpactResidualCoverage.UpperBound,
                    _ => CombatImpactResidualCoverage.Unknown,
                };
                status =
                    difference == 0
                        ? CombatImpactControlStatus.Exact
                        : CombatImpactControlStatus.PositiveResidual;
                if (residualCoverage == CombatImpactResidualCoverage.Unknown)
                    residual = null;
            }
        }

        return new CombatImpactAmountLedger(
            authoritativeTotal,
            observed.Value,
            observed.Coverage,
            observed.ValuedApplicationCount,
            events.Count,
            observed.Unit,
            observed.WeakestValueBasis,
            comparable,
            residual,
            residualCoverage,
            status
        );
    }

    private static CombatImpactValueBasis WeakestValueBasis(
        IReadOnlyList<CombatImpactEvent> events
    ) =>
        events.Any(item => item.ValueBasis == CombatImpactValueBasis.None)
            ? CombatImpactValueBasis.None
        : events.Any(item => item.ValueBasis == CombatImpactValueBasis.NetFrameDelta)
            ? CombatImpactValueBasis.NetFrameDelta
        : events.Any(item => item.ValueBasis == CombatImpactValueBasis.ConfiguredActionAmount)
            ? CombatImpactValueBasis.ConfiguredActionAmount
        : CombatImpactValueBasis.ExactAdjustment;

    private static bool AllKnownValuesExact(IReadOnlyList<CombatImpactEvent> events) =>
        events.All(item => item.ValueBasis == CombatImpactValueBasis.ExactAdjustment);

    private static GroupKey Key(
        CombatImpactKind kind,
        string? nativeAttributeKey,
        CombatImpactEventSurface surface
    ) => new(kind, nativeAttributeKey ?? NativeKey(kind), surface);

    private static int SumValues(IEnumerable<CombatImpactEvent> events)
    {
        long value = 0;
        foreach (var item in events)
            value += item.Value!.Value;
        return value > int.MaxValue ? int.MaxValue
            : value < int.MinValue ? int.MinValue
            : (int)value;
    }

    private static int SaturatingSum(IEnumerable<int> values)
    {
        long total = 0;
        foreach (var value in values)
            total += value;
        return total > int.MaxValue ? int.MaxValue
            : total < int.MinValue ? int.MinValue
            : (int)total;
    }

    internal static string NativeKey(CombatImpactKind kind) =>
        kind switch
        {
            CombatImpactKind.DirectDamage => "DamageAmount",
            CombatImpactKind.Burn => "BurnApplyAmount",
            CombatImpactKind.Poison => "PoisonApplyAmount",
            CombatImpactKind.Healing => "HealAmount",
            CombatImpactKind.Shield => "ShieldApplyAmount",
            CombatImpactKind.Charge => "ChargeAmount",
            CombatImpactKind.Haste => "HasteAmount",
            CombatImpactKind.Slow => "SlowAmount",
            CombatImpactKind.Freeze => "FreezeAmount",
            CombatImpactKind.Flying => "Flying",
            CombatImpactKind.Destroy => "DestroyTargets",
            _ => "Custom_0",
        };

    private static int GroupOrder(CombatImpactKind kind) =>
        kind switch
        {
            CombatImpactKind.DirectDamage => 0,
            CombatImpactKind.Burn => 1,
            CombatImpactKind.Poison => 2,
            CombatImpactKind.Healing => 3,
            CombatImpactKind.Shield => 4,
            CombatImpactKind.Charge => 5,
            CombatImpactKind.Haste => 6,
            CombatImpactKind.Slow => 7,
            CombatImpactKind.Freeze => 8,
            CombatImpactKind.Flying => 9,
            CombatImpactKind.AttributeChange => 10,
            CombatImpactKind.Destroy => 11,
            _ => 99,
        };

    private readonly record struct GroupKey(
        CombatImpactKind Kind,
        string NativeAttributeKey,
        CombatImpactEventSurface Surface
    );

    private readonly record struct ObservedAggregate(
        int? Value,
        CombatImpactValueUnit Unit,
        CombatImpactCoverage Coverage,
        int ValuedApplicationCount = 0,
        CombatImpactValueBasis WeakestValueBasis = CombatImpactValueBasis.None,
        bool AllKnownValuesExact = false
    );

    private readonly record struct CriticalAggregate(
        int Count,
        int OutcomeCount,
        ObservedAggregate Observed
    );

    private readonly record struct TriggerLedger(
        IReadOnlyList<CombatImpactTriggerSource> Sources,
        int UnattributedCount,
        int FallbackCount,
        int NoEvidenceCount,
        int NotApplicableCount,
        CombatImpactTriggerPresentationState PresentationState
    );
}
