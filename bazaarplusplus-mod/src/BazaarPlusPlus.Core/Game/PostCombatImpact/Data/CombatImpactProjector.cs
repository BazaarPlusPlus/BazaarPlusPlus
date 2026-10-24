#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Combat;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

public static class CombatImpactProjector
{
    public static CombatImpactReport Project(
        CombatTimeline simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var transformLineage = CombatImpactTransformLineage.Build(simulation);
        var executions = ProjectExecutions(simulation, entities);
        var events = new List<CombatImpactEvent>();
        var diagnostics = new List<CombatImpactProjectionDiagnostic>();
        var explicitUseApplications = new Dictionary<UseAttributionKey, int>();
        var blockedUseRules = new HashSet<RuleEffectKey>();

        foreach (var originalExecution in executions)
        {
            var execution = originalExecution;
            var sourceResolution = TryResolvePrerequisiteSkillSource(execution, entities);
            if (sourceResolution.Diagnostic != null)
                diagnostics.Add(sourceResolution.Diagnostic);
            if (sourceResolution.Skill != null)
            {
                execution = execution with
                {
                    SourceId = sourceResolution.Skill.Id,
                    PrerequisiteSkillSource = true,
                };
            }

            if (sourceResolution.UseRule != null)
            {
                if (
                    sourceResolution.Skill != null
                    && TryProjectExplicitUseAttribution(
                        execution,
                        sourceResolution.Implementation!,
                        sourceResolution.Skill,
                        sourceResolution.UseRule,
                        entities,
                        out var mappedEvent
                    )
                )
                {
                    events.Add(mappedEvent);
                    var key = new UseAttributionKey(
                        sourceResolution.Implementation!.Id,
                        sourceResolution.UseRule.SourceRule.EffectId,
                        execution.TriggerSourceId!,
                        sourceResolution.Skill.Id
                    );
                    explicitUseApplications[key] =
                        explicitUseApplications.GetValueOrDefault(key) + 1;
                    continue;
                }

                var implementation = sourceResolution.Implementation!;
                blockedUseRules.Add(
                    new RuleEffectKey(
                        implementation.Id,
                        sourceResolution.UseRule.SourceRule.EffectId
                    )
                );
                if (sourceResolution.Skill != null)
                {
                    diagnostics.Add(
                        new CombatImpactProjectionDiagnostic(
                            CombatImpactProjectionDiagnosticKind.RuleExecutionMismatch,
                            implementation.Id,
                            sourceResolution.UseRule.SourceRule.EffectId,
                            execution.TriggerSourceId
                        )
                    );
                }
                execution = originalExecution;
            }

            if (
                execution.SourceId == null
                || execution.TargetId == null
                || !execution.Kind.HasValue
            )
                continue;

            var kind = execution.Kind.Value;
            var resolved = execution.Resolved;
            if (kind == CombatImpactKind.AttributeChange && !IsDisplayableAttributeChange(resolved))
                continue;
            var triggerProvenance = ResolveTriggerProvenance(execution, entities);

            events.Add(
                new CombatImpactEvent(
                    kind,
                    execution.SourceId,
                    execution.TargetId,
                    resolved.Value.HasValue ? SaturatingInt(resolved.Value.Value) : null,
                    resolved.Unit,
                    resolved.NativeAttributeKey,
                    resolved.IsCritical,
                    resolved.Basis
                )
                {
                    Surface = resolved.Surface,
                    OccurrenceBasis = CombatImpactOccurrenceBasis.ExplicitExecution,
                    CriticalCount = resolved.CriticalCount,
                    CriticalOutcomeCount = resolved.CriticalOutcomeCount,
                    CriticalValue = resolved.CriticalValue,
                    NonCriticalValue = resolved.NonCriticalValue,
                    AlternateNonCriticalValue = resolved.AlternateNonCriticalValue,
                    HasCriticalAdjustmentCandidate = resolved.HasCriticalAdjustmentCandidate,
                    IsCritCapable = execution.IsCritCapable,
                    RawDirectSourceId = execution.DirectSourceId,
                    TriggerSourceId = execution.TriggerSourceId,
                    TriggerFrameIndex = execution.FrameIndex,
                    EffectId = execution.EffectId,
                    ExecutionContextId = execution.ExecutionContextId,
                    ActivitySourceResolution = triggerProvenance.SourceResolution,
                    TriggerScope = triggerProvenance.Scope,
                    IsUnattributedTransitionClaimant = resolved.IsUnattributedTransitionClaimant,
                    UnattributedTransitionValue = resolved.UnattributedTransitionValue,
                    AttributeTransitionNetValue = resolved.AttributeTransitionNetValue,
                    AttributeTransitionResolution = resolved.AttributeTransitionResolution,
                    AttributeTransitionFailureReasons = resolved.AttributeTransitionFailureReasons,
                    AttributeTransitionUnresolvedClaimantCount =
                        resolved.AttributeTransitionUnresolvedClaimantCount,
                    AttributeTransitionEventOrderReplayReconciles =
                        resolved.AttributeTransitionEventOrderReplayReconciles,
                    AttributeTransitionEventOrderReplayIncludesMultiply =
                        resolved.AttributeTransitionEventOrderReplayIncludesMultiply,
                }
            );
        }

        AddLifestealEvents(simulation, entities, executions, events);
        AddCardActionCostEvents(simulation, entities, events);
        AddAuraAttributeEvents(simulation, entities, events, diagnostics);

        var useCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var authoritative = new Dictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>>(
            StringComparer.Ordinal
        );
        foreach (var (sourceId, stats) in simulation.CardStats)
        {
            if (stats.TryGetValue(CombatCardStat.UseCount, out var uses))
                useCounts[sourceId] = uses;

            var metrics = new List<CombatImpactAuthoritativeMetric>();
            AddAmountMetric(
                stats,
                CombatCardStat.DamageDone,
                CombatImpactKind.DirectDamage,
                CombatImpactAggregator.NativeKey(CombatImpactKind.DirectDamage),
                metrics
            );
            AddAmountMetric(
                stats,
                CombatCardStat.BurnAdded,
                CombatImpactKind.Burn,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
                metrics
            );
            AddAmountMetric(
                stats,
                CombatCardStat.PoisonAdded,
                CombatImpactKind.Poison,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Poison),
                metrics
            );
            AddAmountMetric(
                stats,
                CombatCardStat.HealAdded,
                CombatImpactKind.Healing,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Healing),
                metrics
            );
            AddAmountMetric(
                stats,
                CombatCardStat.ShieldAdded,
                CombatImpactKind.Shield,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Shield),
                metrics
            );
            AddApplicationMetric(
                stats,
                CombatCardStat.HastedCardsCount,
                CombatImpactKind.Haste,
                metrics
            );
            AddApplicationMetric(
                stats,
                CombatCardStat.SlowedCardsCount,
                CombatImpactKind.Slow,
                metrics
            );
            AddApplicationMetric(
                stats,
                CombatCardStat.FrozenCardsCount,
                CombatImpactKind.Freeze,
                metrics
            );
            AddAmountMetric(
                stats,
                CombatCardStat.RegenAdded,
                CombatImpactKind.AttributeChange,
                "RegenApplyAmount",
                metrics
            );
            AddAmountMetric(
                stats,
                CombatCardStat.RageAdded,
                CombatImpactKind.AttributeChange,
                "RageApplyAmount",
                metrics
            );
            AddAmountMetric(
                stats,
                CombatCardStat.TempoAdded,
                CombatImpactKind.AttributeChange,
                "TempoApplyAmount",
                metrics
            );
            AddAmountMetric(
                stats,
                CombatCardStat.TempoSpent,
                CombatImpactKind.AttributeChange,
                "TempoRemoveAmount",
                metrics
            );
            if (metrics.Count > 0)
            {
                authoritative[sourceId] = metrics;
                if (
                    entities.TryGetValue(sourceId, out var metricSource)
                    && metricSource.TypeLabel is not ("Item" or "Skill")
                )
                {
                    diagnostics.AddRange(
                        metrics.Select(metric => new CombatImpactProjectionDiagnostic(
                            CombatImpactProjectionDiagnosticKind.NonDisplayableAuthoritativeMetric,
                            sourceId,
                            metric.NativeAttributeKey
                        ))
                    );
                }
            }
        }

        AddUseAttributionEvents(
            entities,
            useCounts,
            explicitUseApplications,
            blockedUseRules,
            events,
            diagnostics
        );
        var criticalTriggerEvidenceAudit = AttachCriticalTriggerEvidence(
            events,
            executions,
            entities
        );
        AttachNativeActivationCriticalCounts(events);
        RecoverAmbiguousDamageCriticals(events, authoritative);

        var canonicalEvents = CanonicalizeEvents(events, transformLineage, entities);
        var canonicalUseCounts = CanonicalizeUseCounts(useCounts, transformLineage, entities);
        var canonicalAuthoritative = CanonicalizeAuthoritativeMetrics(
            authoritative,
            transformLineage,
            entities
        );
        var canonicalAttributeTransitionResiduals = ProjectAttributeTransitionResiduals(events)
            .Select(item =>
                item with
                {
                    TargetId = transformLineage.ResolveKnown(item.TargetId, entities),
                }
            )
            .ToArray();
        var attributeTransitionDiagnostics = ProjectAttributeTransitionDiagnostics(events);

        var report = CombatImpactAggregator.Aggregate(
            new CombatImpactProjectionInput(
                entities.ToDictionary(
                    pair => pair.Key,
                    pair => (CombatImpactLedgerEntity)pair.Value,
                    StringComparer.Ordinal
                ),
                canonicalEvents,
                canonicalUseCounts,
                canonicalAuthoritative
            )
            {
                AttributeTransitionResiduals = canonicalAttributeTransitionResiduals,
            }
        );
        report = AttachPeriodicImpacts(
            report,
            PeriodicEffectAttribution.Project(simulation, entities),
            transformLineage,
            entities
        );
        return report with
        {
            ProjectionDiagnostics = diagnostics.Distinct().ToArray(),
            AttributeTransitionDiagnostics = attributeTransitionDiagnostics,
            CriticalTriggerEvidenceAudit = criticalTriggerEvidenceAudit,
        };
    }

    private static IReadOnlyList<CombatImpactEvent> CanonicalizeEvents(
        IEnumerable<CombatImpactEvent> events,
        CombatImpactTransformLineage lineage,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        events
            .Select(item =>
                item with
                {
                    SourceId = lineage.ResolveKnown(item.SourceId, entities),
                    TargetId = lineage.ResolveKnown(item.TargetId, entities),
                    RawDirectSourceId = lineage.ResolveOptional(item.RawDirectSourceId, entities),
                    TriggerSourceId = lineage.ResolveOptional(item.TriggerSourceId, entities),
                }
            )
            .ToArray();

    private static IReadOnlyDictionary<string, int> CanonicalizeUseCounts(
        IReadOnlyDictionary<string, int> useCounts,
        CombatImpactTransformLineage lineage,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        useCounts
            .GroupBy(item => lineage.ResolveKnown(item.Key, entities), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => SaturatingInt(group.Sum(item => (long)item.Value)),
                StringComparer.Ordinal
            );

    private static IReadOnlyDictionary<
        string,
        IReadOnlyList<CombatImpactAuthoritativeMetric>
    > CanonicalizeAuthoritativeMetrics(
        IReadOnlyDictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>> metrics,
        CombatImpactTransformLineage lineage,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        metrics
            .SelectMany(source =>
                source.Value.Select(metric => new
                {
                    SourceId = lineage.ResolveKnown(source.Key, entities),
                    Metric = metric,
                })
            )
            .GroupBy(item => item.SourceId, StringComparer.Ordinal)
            .ToDictionary(
                source => source.Key,
                source =>
                    (IReadOnlyList<CombatImpactAuthoritativeMetric>)
                        source
                            .GroupBy(item => new
                            {
                                item.Metric.Kind,
                                item.Metric.NativeAttributeKey,
                                item.Metric.Unit,
                                item.Metric.Basis,
                                item.Metric.CanReconcileApplicationCount,
                            })
                            .Select(group =>
                            {
                                var key = group.Key;
                                return new CombatImpactAuthoritativeMetric(
                                    key.Kind,
                                    key.NativeAttributeKey,
                                    SaturatingInt(group.Sum(item => (long)item.Metric.Value)),
                                    key.Unit,
                                    key.Basis,
                                    key.CanReconcileApplicationCount
                                );
                            })
                            .ToArray(),
                StringComparer.Ordinal
            );

    private static void AddUseAttributionEvents(
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, int> useCounts,
        IReadOnlyDictionary<UseAttributionKey, int> explicitApplications,
        ISet<RuleEffectKey> blockedRules,
        ICollection<CombatImpactEvent> events,
        ICollection<CombatImpactProjectionDiagnostic> diagnostics
    )
    {
        foreach (var implementation in entities.Values)
        {
            if (
                implementation.TypeLabel != "SocketEffect"
                || implementation.UseAttributionRules is not { Count: > 0 }
                || implementation.CombatantId is not { } combatantId
                || implementation.Section != CardSection.Hand
                || !implementation.SocketId.HasValue
            )
                continue;

            foreach (
                var ruleGroup in implementation.UseAttributionRules.GroupBy(
                    rule => rule.SourceRule.EffectId,
                    StringComparer.Ordinal
                )
            )
            {
                if (ruleGroup.Take(2).Count() != 1)
                    continue;
                var rule = ruleGroup.Single();
                if (
                    blockedRules.Contains(
                        new RuleEffectKey(implementation.Id, rule.SourceRule.EffectId)
                    )
                )
                    continue;

                var skills = FindPrerequisiteSkills(implementation, rule.SourceRule, entities);
                if (skills.Length == 0)
                    continue;
                if (skills.Length > 1)
                {
                    diagnostics.Add(
                        new CombatImpactProjectionDiagnostic(
                            CombatImpactProjectionDiagnosticKind.AmbiguousPrerequisiteSkill,
                            implementation.Id,
                            rule.SourceRule.EffectId
                        )
                    );
                    continue;
                }

                var skill = skills[0];
                foreach (
                    var item in entities.Values.Where(entity =>
                        entity.TypeLabel == "Item"
                        && entity.CombatantId == combatantId
                        && entity.Section == CardSection.Hand
                        && entity.SocketId.HasValue
                        && rule.ItemCondition.Matches(entity)
                        && SpansOverlap(entity, implementation)
                    )
                )
                {
                    var key = new UseAttributionKey(
                        implementation.Id,
                        rule.SourceRule.EffectId,
                        item.Id,
                        skill.Id
                    );
                    var explicitCount = explicitApplications.GetValueOrDefault(key);
                    if (!useCounts.TryGetValue(item.Id, out var uses))
                    {
                        if (explicitCount > 0)
                        {
                            diagnostics.Add(
                                new CombatImpactProjectionDiagnostic(
                                    CombatImpactProjectionDiagnosticKind.MissingUseCount,
                                    implementation.Id,
                                    rule.SourceRule.EffectId,
                                    item.Id
                                )
                            );
                        }
                        continue;
                    }
                    if (explicitCount > uses)
                    {
                        diagnostics.Add(
                            new CombatImpactProjectionDiagnostic(
                                CombatImpactProjectionDiagnosticKind.ExplicitApplicationsExceedUseCount,
                                implementation.Id,
                                rule.SourceRule.EffectId,
                                item.Id
                            )
                        );
                        continue;
                    }

                    for (var index = explicitCount; index < uses; index++)
                    {
                        events.Add(
                            CreateUseAttributionEvent(
                                implementation,
                                skill,
                                item,
                                rule,
                                frameIndex: null,
                                CombatImpactOccurrenceBasis.ReconstructedTransition
                            )
                        );
                    }
                }
            }
        }
    }

    private static bool SpansOverlap(CombatImpactEntity left, CombatImpactEntity right)
    {
        var leftStart = CardFacetOrder.SocketRank(left.SocketId!.Value);
        var leftEnd = leftStart + Math.Max(1, left.DisplaySpan);
        var rightStart = CardFacetOrder.SocketRank(right.SocketId!.Value);
        var rightEnd = rightStart + Math.Max(1, right.DisplaySpan);
        return leftStart < rightEnd && rightStart < leftEnd;
    }

    private static CombatImpactReport AttachPeriodicImpacts(
        CombatImpactReport report,
        PeriodicAttributionReport periodic,
        CombatImpactTransformLineage lineage,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        return report with
        {
            Sources = report
                .Sources.Select(source =>
                    source with
                    {
                        Groups = source
                            .Groups.Select(group =>
                            {
                                var kind = PeriodicKind(group);
                                var impact = kind.HasValue
                                    ? RollUpPeriodicImpact(
                                        periodic.SourceImpacts,
                                        source.Entity.Id,
                                        kind.Value,
                                        group
                                            .Targets.Select(target =>
                                                ((CombatImpactEntity)target.Entity).CombatantId
                                            )
                                            .OfType<Combatant>()
                                            .ToHashSet(),
                                        lineage,
                                        entities
                                    )
                                    : null;
                                return impact != null
                                    ? group with
                                    {
                                        PeriodicImpact = impact,
                                    }
                                    : group;
                            })
                            .ToArray(),
                    }
                )
                .ToArray(),
        };
    }

    private static CombatImpactPeriodicImpact? RollUpPeriodicImpact(
        IReadOnlyDictionary<PeriodicImpactKey, CombatImpactPeriodicImpact> sourceImpacts,
        string sourceId,
        CombatImpactPeriodicKind kind,
        IReadOnlyCollection<Combatant> targetCombatants,
        CombatImpactTransformLineage lineage,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var matches = sourceImpacts
            .Where(pair =>
                string.Equals(
                    lineage.ResolveKnown(pair.Key.SourceId, entities),
                    sourceId,
                    StringComparison.Ordinal
                )
                && pair.Key.Kind == kind
                && (targetCombatants.Count == 0 || targetCombatants.Contains(pair.Key.Combatant))
            )
            .Select(pair => pair.Value)
            .ToArray();
        if (matches.Length == 0)
            return null;

        return new CombatImpactPeriodicImpact(
            SaturatingInt(matches.Sum(impact => (long)impact.HealthAmount)),
            SaturatingInt(matches.Sum(impact => (long)impact.ShieldAmount)),
            matches.Any(impact => impact.Proof == CombatImpactPeriodicProof.Proportional)
                ? CombatImpactPeriodicProof.Proportional
                : CombatImpactPeriodicProof.Exact,
            PeriodicEffectAttribution.ModelVersion
        );
    }

    private static CombatImpactPeriodicKind? PeriodicKind(CombatImpactGroup group) =>
        group.Surface != CombatImpactEventSurface.AppliedEffect
            ? null
            : group.Kind switch
            {
                CombatImpactKind.Burn
                    when group.NativeAttributeKey
                        == CombatImpactAggregator.NativeKey(CombatImpactKind.Burn) =>
                    CombatImpactPeriodicKind.Burn,
                CombatImpactKind.Poison
                    when group.NativeAttributeKey
                        == CombatImpactAggregator.NativeKey(CombatImpactKind.Poison) =>
                    CombatImpactPeriodicKind.Poison,
                CombatImpactKind.AttributeChange
                    when group.NativeAttributeKey == "RegenApplyAmount" =>
                    CombatImpactPeriodicKind.Regen,
                _ => null,
            };

    private static IReadOnlyList<ProjectedExecution> ProjectExecutions(
        CombatTimeline simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var projections = new List<ProjectedExecution>();
        var cardAttributes = CreateCardAttributeTimeline(entities);
        for (var frameIndex = 0; frameIndex < simulation.Frames.Count; frameIndex++)
        {
            var frame = simulation.Frames[frameIndex];
            ReconcileCardAttributeTimeline(frame, cardAttributes, usePreviousValue: true);
            var executed = frame.Events.OfType<CombatEffectExecuted>().ToArray();
            foreach (var item in executed)
            {
                var attributedSourceId = ResolveActivitySource(
                    item.Source,
                    item.TriggerSource,
                    entities
                );
                var isCritCapable = IsCritCapable(
                    item.Source,
                    attributedSourceId,
                    item.EffectId,
                    entities
                );
                var targetId = ResolveTargetId(item.Target);
                var hasKind = TryResolveKind(item.ActionType, out var kind);
                var configuredActionValue = default(ResolvedImpactValue);
                var hasConfiguredDuration =
                    hasKind
                    && attributedSourceId != null
                    && TryResolveConfiguredDurationAction(
                        item.ActionType,
                        attributedSourceId,
                        cardAttributes,
                        out configuredActionValue
                    );
                var concurrentHealthAdjustment = default(ResolvedImpactValue);
                var hasConcurrentHealthAdjustment =
                    hasKind
                    && TryResolveConcurrentHealthAdjustment(
                        frame,
                        item,
                        kind,
                        executed,
                        out concurrentHealthAdjustment
                    );
                var resolved =
                    hasConfiguredDuration ? configuredActionValue
                    : hasConcurrentHealthAdjustment ? concurrentHealthAdjustment
                    : hasKind
                        ? ResolveValue(
                            frame,
                            item,
                            kind,
                            IsTransitionUnique(frame, executed, item, entities),
                            executed,
                            entities,
                            cardAttributes
                        )
                    : default;
                if (
                    hasKind
                    && kind == CombatImpactKind.DirectDamage
                    && HasCriticalHealthAdjustment(frame, item, kind)
                )
                    resolved = resolved with { HasCriticalAdjustmentCandidate = true };
                if (
                    attributedSourceId != null
                    && TryResolveNonCriticalValue(
                        item.ActionType,
                        attributedSourceId,
                        cardAttributes,
                        frame,
                        out var nonCriticalValue,
                        out var alternateNonCriticalValue
                    )
                )
                {
                    resolved = resolved with
                    {
                        NonCriticalValue = nonCriticalValue,
                        AlternateNonCriticalValue = alternateNonCriticalValue,
                    };
                    resolved = ResolveConfiguredStatusCriticalOutcome(
                        item.ActionType,
                        resolved,
                        isCritCapable
                    );
                }
                projections.Add(
                    new ProjectedExecution(
                        attributedSourceId,
                        targetId,
                        hasKind ? kind : null,
                        resolved,
                        item.Source,
                        item.TriggerSource,
                        frameIndex,
                        item.EffectId,
                        item.ActionType,
                        item.ExecutionContextId
                    )
                    {
                        IsCritCapable = isCritCapable,
                    }
                );
            }
            ReconcileCardAttributeTimeline(frame, cardAttributes, usePreviousValue: false);
        }
        return projections;
    }

    private static IReadOnlyList<CombatImpactAttributeTransitionResidual> ProjectAttributeTransitionResiduals(
        IReadOnlyList<CombatImpactEvent> events
    ) =>
        events
            .Where(item =>
                item.Kind == CombatImpactKind.AttributeChange
                && item.Surface
                    is CombatImpactEventSurface.CardAttribute
                        or CombatImpactEventSurface.PlayerAttribute
                && (
                    item.IsUnattributedTransitionClaimant
                    || item.AttributeTransitionResolution
                        == CombatImpactAttributeTransitionResolution.AuraOverlap
                )
                && item.TriggerFrameIndex.HasValue
                && item.UnattributedTransitionValue.HasValue
            )
            .GroupBy(item => new AttributeTransitionResidualKey(
                item.TriggerFrameIndex!.Value,
                item.TargetId,
                item.NativeAttributeKey ?? CombatImpactAggregator.NativeKey(item.Kind),
                item.Unit
            ))
            .Select(group =>
            {
                var values = group
                    .Select(item => item.UnattributedTransitionValue!.Value)
                    .Distinct()
                    .Take(2)
                    .ToArray();
                if (values.Length != 1)
                    return null;
                var auraOverlap = group.Any(item =>
                    item.AttributeTransitionResolution
                    == CombatImpactAttributeTransitionResolution.AuraOverlap
                );
                return new CombatImpactAttributeTransitionResidual(
                    group.Key.FrameIndex,
                    group.Key.TargetId,
                    group.Key.NativeAttributeKey,
                    auraOverlap
                        ? SaturatingInt(
                            (long)values[0]
                                - group.Sum(item => (long)item.Value.GetValueOrDefault())
                        )
                        : values[0],
                    group.Key.Unit,
                    group.Count(),
                    auraOverlap
                        ? CombatImpactAttributeTransitionResidualReason.AuraOverlap
                        : CombatImpactAttributeTransitionResidualReason.ConcurrentAttributionUnavailable
                )
                {
                    Surface = group.First().Surface,
                };
            })
            .Where(residual => residual != null)
            .Cast<CombatImpactAttributeTransitionResidual>()
            .ToArray();

    private static IReadOnlyList<CombatImpactAttributeTransitionDiagnostic> ProjectAttributeTransitionDiagnostics(
        IReadOnlyList<CombatImpactEvent> events
    ) =>
        events
            .Where(item =>
                item.AttributeTransitionResolution.HasValue
                && item.AttributeTransitionNetValue.HasValue
                && item.TriggerFrameIndex.HasValue
            )
            .GroupBy(item => new AttributeTransitionResidualKey(
                item.TriggerFrameIndex!.Value,
                item.TargetId,
                item.NativeAttributeKey ?? CombatImpactAggregator.NativeKey(item.Kind),
                item.Unit
            ))
            .Select(group =>
            {
                var netValues = group
                    .Select(item => item.AttributeTransitionNetValue!.Value)
                    .Distinct()
                    .Take(2)
                    .ToArray();
                if (netValues.Length != 1)
                    return null;

                var resolution =
                    group.Any(item =>
                        item.AttributeTransitionResolution
                        == CombatImpactAttributeTransitionResolution.AuraOverlap
                    )
                        ? CombatImpactAttributeTransitionResolution.AuraOverlap
                    : group.Any(item =>
                        item.AttributeTransitionResolution
                        == CombatImpactAttributeTransitionResolution.ConcurrentResidual
                    )
                        ? CombatImpactAttributeTransitionResolution.ConcurrentResidual
                    : group.Any(item =>
                        item.AttributeTransitionResolution
                        == CombatImpactAttributeTransitionResolution.ConcurrentSingleUnknownSolved
                    )
                        ? CombatImpactAttributeTransitionResolution.ConcurrentSingleUnknownSolved
                    : group.Any(item =>
                        item.AttributeTransitionResolution
                        == CombatImpactAttributeTransitionResolution.ConcurrentConfiguredExact
                    )
                        ? CombatImpactAttributeTransitionResolution.ConcurrentConfiguredExact
                    : CombatImpactAttributeTransitionResolution.SingleClaimantNet;
                var attributedValue = SaturatingInt(
                    group.Where(item => item.Value.HasValue).Sum(item => (long)item.Value!.Value)
                );
                var residualValue = resolution
                    is CombatImpactAttributeTransitionResolution.ConcurrentResidual
                        or CombatImpactAttributeTransitionResolution.AuraOverlap
                    ? SaturatingInt((long)netValues[0] - attributedValue)
                    : 0;
                return new CombatImpactAttributeTransitionDiagnostic(
                    group.Key.FrameIndex,
                    group.Key.TargetId,
                    group.Key.NativeAttributeKey,
                    netValues[0],
                    attributedValue,
                    residualValue,
                    group.Count(),
                    group.Max(item => item.AttributeTransitionUnresolvedClaimantCount),
                    group.Key.Unit,
                    resolution,
                    group
                        .SelectMany(item => item.AttributeTransitionFailureReasons)
                        .Distinct()
                        .OrderBy(reason => reason)
                        .ToArray(),
                    group.Any(item => item.AttributeTransitionEventOrderReplayReconciles),
                    group.Any(item => item.AttributeTransitionEventOrderReplayIncludesMultiply)
                );
            })
            .Where(diagnostic => diagnostic != null)
            .Cast<CombatImpactAttributeTransitionDiagnostic>()
            .OrderBy(diagnostic => diagnostic.FrameIndex)
            .ThenBy(diagnostic => diagnostic.TargetId, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.NativeAttributeKey, StringComparer.Ordinal)
            .ToArray();

    private static void AddLifestealEvents(
        CombatTimeline simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyList<ProjectedExecution> executions,
        ICollection<CombatImpactEvent> events
    )
    {
        var cardAttributes = CreateCardAttributeTimeline(entities);
        var executionsByFrame = executions
            .GroupBy(execution => execution.FrameIndex)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProjectedExecution>)group.ToArray()
            );
        for (var frameIndex = 0; frameIndex < simulation.Frames.Count; frameIndex++)
        {
            var frame = simulation.Frames[frameIndex];
            var frameExecutions = executionsByFrame.GetValueOrDefault(frameIndex) ?? [];
            ReconcileCardAttributeTimeline(frame, cardAttributes, usePreviousValue: true);
            AddLifestealEvents(
                Combatant.Player,
                frame.PlayerUpdates,
                frameIndex,
                entities,
                cardAttributes,
                frameExecutions,
                events
            );
            AddLifestealEvents(
                Combatant.Opponent,
                frame.OpponentUpdates,
                frameIndex,
                entities,
                cardAttributes,
                frameExecutions,
                events
            );
            ReconcileCardAttributeTimeline(frame, cardAttributes, usePreviousValue: false);
        }
    }

    private static void AddLifestealEvents(
        Combatant combatant,
        CombatPlayerUpdate? update,
        int frameIndex,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes,
        IReadOnlyList<ProjectedExecution> frameExecutions,
        ICollection<CombatImpactEvent> events
    )
    {
        var realizedHealing = ResolveUnrecordedHealthGain(update);
        if (realizedHealing <= 0)
            return;

        var opposingCombatant =
            combatant == Combatant.Player ? Combatant.Opponent : Combatant.Player;
        var candidates = frameExecutions
            .Where(execution =>
                execution.ActionType == CombatActionType.PlayerDamage
                && execution.SourceId != null
                && execution.TargetId == PlayerId(opposingCombatant)
                && execution.Resolved.Value is > 0
                && entities.TryGetValue(execution.SourceId, out var source)
                && source.TypeLabel == "Item"
                && source.CombatantId == combatant
                && cardAttributes.TryGetValue(execution.SourceId, out var attributes)
                && attributes.GetValueOrDefault(CombatCardAttribute.Lifesteal) > 0
            )
            .GroupBy(execution => execution.SourceId!, StringComparer.Ordinal)
            .Select(group => new LifestealCandidate(
                group.Key,
                SaturatingInt(group.Sum(execution => (long)execution.Resolved.Value!.Value)),
                group.First()
            ))
            .Where(candidate => candidate.Damage > 0)
            .ToArray();
        if (candidates.Length == 0)
            return;

        var totalDamage = SaturatingInt(candidates.Sum(candidate => (long)candidate.Damage));
        if (candidates.Length > 1 && realizedHealing != totalDamage)
            return;

        foreach (var candidate in candidates)
        {
            var amount =
                candidates.Length == 1
                    ? Math.Min(realizedHealing, candidate.Damage)
                    : candidate.Damage;
            if (amount <= 0)
                continue;

            var provenance = ResolveTriggerProvenance(candidate.Execution, entities);
            events.Add(
                new CombatImpactEvent(
                    CombatImpactKind.Healing,
                    candidate.SourceId,
                    PlayerId(combatant),
                    amount,
                    CombatImpactValueUnit.Amount,
                    CombatImpactAggregator.NativeKey(CombatImpactKind.Healing),
                    false,
                    CombatImpactValueBasis.NetFrameDelta
                )
                {
                    OccurrenceBasis = CombatImpactOccurrenceBasis.ReconstructedTransition,
                    RawDirectSourceId = candidate.Execution.DirectSourceId,
                    TriggerSourceId = candidate.Execution.TriggerSourceId,
                    TriggerFrameIndex = frameIndex,
                    ActivitySourceResolution = provenance.SourceResolution,
                    TriggerScope = provenance.Scope,
                }
            );
        }
    }

    private static int ResolveUnrecordedHealthGain(CombatPlayerUpdate? update)
    {
        if (
            update == null
            || !update.Attributes.TryGetValue(CombatPlayerAttribute.Health, out var health)
        )
            return 0;

        var recordedHealthMovement = update
            .HealthAdjustments.Where(adjustment =>
                adjustment.AttributeChanged == CombatHealthChange.Health
            )
            .Sum(adjustment => (long)adjustment.Amount);
        var residual = (long)health.Delta - recordedHealthMovement;
        return residual > 0 ? SaturatingInt(residual) : 0;
    }

    private static void AddCardActionCostEvents(
        CombatTimeline simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        ICollection<CombatImpactEvent> events
    )
    {
        foreach (var frame in simulation.Frames)
        {
            foreach (var candidate in frame.Events)
            {
                if (
                    candidate is not CombatCardActionCostSpent costSpent
                    || costSpent.PlayerAttributeSpent != CombatPlayerAttribute.Tempo
                )
                    continue;

                var sourceId = costSpent.ExecutingCard;
                var targetId = ResolveCardActionCostTargetId(costSpent, entities);
                if (
                    !entities.TryGetValue(sourceId, out var source)
                    || source.TypeLabel is not ("Item" or "Skill")
                    || targetId == null
                )
                    continue;

                events.Add(
                    new CombatImpactEvent(
                        CombatImpactKind.AttributeChange,
                        sourceId,
                        targetId,
                        null,
                        CombatImpactValueUnit.Amount,
                        "TempoRemoveAmount",
                        ValueBasis: CombatImpactValueBasis.None
                    )
                    {
                        OccurrenceBasis = CombatImpactOccurrenceBasis.ExplicitExecution,
                    }
                );
            }
        }
    }

    private static Dictionary<
        string,
        Dictionary<CombatCardAttribute, int>
    > CreateCardAttributeTimeline(IReadOnlyDictionary<string, CombatImpactEntity> entities)
    {
        var timeline = new Dictionary<string, Dictionary<CombatCardAttribute, int>>(
            StringComparer.Ordinal
        );
        foreach (var entity in entities.Values)
        {
            if (entity.Attributes == null)
                continue;
            timeline[entity.Id] = new Dictionary<CombatCardAttribute, int>(entity.Attributes);
        }

        return timeline;
    }

    private static void ReconcileCardAttributeTimeline(
        CombatFrame frame,
        IDictionary<string, Dictionary<CombatCardAttribute, int>> timeline,
        bool usePreviousValue
    )
    {
        foreach (var (instanceId, cardUpdate) in frame.CardUpdates)
        {
            var cardId = instanceId;
            if (!timeline.TryGetValue(cardId, out var attributes))
            {
                attributes = new Dictionary<CombatCardAttribute, int>();
                timeline[cardId] = attributes;
            }

            foreach (var update in cardUpdate.Attributes.Values)
            {
                attributes[update.AttributeType] = usePreviousValue
                    ? update.PreviousValue
                    : update.CurrentValue;
            }
        }
    }

    private static bool TryResolveConfiguredDurationAction(
        CombatActionType action,
        string sourceId,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes,
        out ResolvedImpactValue resolved
    )
    {
        (CombatCardAttribute Attribute, CombatImpactKind Kind)? configuration = action switch
        {
            CombatActionType.CardCharge => (
                CombatCardAttribute.ChargeAmount,
                CombatImpactKind.Charge
            ),
            CombatActionType.CardHaste => (CombatCardAttribute.HasteAmount, CombatImpactKind.Haste),
            CombatActionType.CardSlow => (CombatCardAttribute.SlowAmount, CombatImpactKind.Slow),
            CombatActionType.CardFreeze => (
                CombatCardAttribute.FreezeAmount,
                CombatImpactKind.Freeze
            ),
            _ => null,
        };
        if (!configuration.HasValue)
        {
            resolved = default;
            return false;
        }

        var (attribute, kind) = configuration.Value;
        resolved = ResolvedImpactValue.Empty(kind, action);

        // Target frame deltas include fixed ticking and cannot reliably split concurrent actions.
        // The source value is the nominal outgoing duration, before target-side mitigation,
        // immunity, overlap, or truncation.
        if (
            cardAttributes.TryGetValue(sourceId, out var attributes)
            && attributes.TryGetValue(attribute, out var configuredAmount)
            && configuredAmount > 0
        )
        {
            resolved = new ResolvedImpactValue(
                configuredAmount,
                CombatImpactValueUnit.Milliseconds,
                CombatImpactAggregator.NativeKey(kind),
                false,
                CombatImpactValueBasis.ConfiguredActionAmount
            );
        }

        return true;
    }

    private static bool TryResolveNonCriticalValue(
        CombatActionType action,
        string sourceId,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes,
        CombatFrame frame,
        out int value,
        out int? alternateValue
    )
    {
        var attribute = action switch
        {
            CombatActionType.PlayerDamage => CombatCardAttribute.DamageAmount,
            CombatActionType.PlayerBurnApply => CombatCardAttribute.BurnApplyAmount,
            CombatActionType.PlayerPoisonApply => CombatCardAttribute.PoisonApplyAmount,
            CombatActionType.PlayerRegenApply => CombatCardAttribute.RegenApplyAmount,
            CombatActionType.PlayerTempoApply => CombatCardAttribute.TempoApplyAmount,
            CombatActionType.PlayerTempoRemove => CombatCardAttribute.TempoRemoveAmount,
            _ => (CombatCardAttribute?)null,
        };
        if (
            attribute.HasValue
            && cardAttributes.TryGetValue(sourceId, out var attributes)
            && attributes.TryGetValue(attribute.Value, out value)
            && value > 0
        )
        {
            alternateValue = null;
            var sourceInstance = (sourceId);
            if (
                HasCriticalConfiguredAmount(action)
                && frame.CardUpdates.TryGetValue(sourceInstance, out var update)
                && update.Attributes.TryGetValue(attribute.Value, out var attributeUpdate)
                && attributeUpdate.CurrentValue > 0
                && attributeUpdate.CurrentValue != value
            )
                alternateValue = attributeUpdate.CurrentValue;
            return true;
        }

        value = 0;
        alternateValue = null;
        return false;
    }

    private static bool HasCriticalConfiguredAmount(CombatActionType action) =>
        action
            is CombatActionType.PlayerDamage
                or CombatActionType.PlayerBurnApply
                or CombatActionType.PlayerPoisonApply
                or CombatActionType.PlayerRegenApply;

    private static ResolvedImpactValue ResolveConfiguredStatusCriticalOutcome(
        CombatActionType action,
        ResolvedImpactValue resolved,
        bool isCritCapable
    )
    {
        if (
            !isCritCapable
            || action
                is not (
                    CombatActionType.PlayerBurnApply
                    or CombatActionType.PlayerPoisonApply
                    or CombatActionType.PlayerRegenApply
                )
            || resolved.Basis != CombatImpactValueBasis.NetFrameDelta
            || resolved.Value is not > 0
            || resolved.NonCriticalValue is not > 0
        )
            return resolved;

        var observed = resolved.Value.Value;
        var matchesNonCritical = observed == resolved.NonCriticalValue.Value;
        var matchesCritical = (long)observed == (long)resolved.NonCriticalValue.Value * 2L;
        if (resolved.AlternateNonCriticalValue is > 0 and var alternate)
        {
            matchesNonCritical |= observed == alternate;
            matchesCritical |= (long)observed == (long)alternate * 2L;
        }

        // A same-frame amount change can make one delta both an old-value critical and a
        // new-value non-critical. Keep that execution unknown instead of choosing an outcome.
        if (matchesCritical == matchesNonCritical)
            return resolved;

        return resolved with
        {
            IsCritical = matchesCritical,
            CriticalCount = matchesCritical ? 1 : 0,
            CriticalOutcomeCount = 1,
            CriticalValue = matchesCritical ? observed : null,
        };
    }

    private static CombatImpactCriticalTriggerEvidenceAudit AttachCriticalTriggerEvidence(
        IList<CombatImpactEvent> events,
        IReadOnlyList<ProjectedExecution> executions,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var observed = ObserveCriticalTriggerExecutions(executions, entities);
        if (observed.Count == 0)
            return new CombatImpactCriticalTriggerEvidenceAudit(0, 0)
            {
                InputEventCount = events.Count,
                InputExecutionCount = executions.Count,
                WorkUnitCount = executions.Count,
            };

        var index = new CriticalTriggerAttributionIndex(events, executions, observed);
        NormalizeCriticalTriggerExecutions(events, index.ListenerEventIndices);

        var origins = new List<CriticalTriggerEvidenceKey>();
        var originSet = new HashSet<CriticalTriggerEvidenceKey>();
        var excludedCandidates = new HashSet<CriticalTriggerOriginExecutionKey>();
        foreach (var evidence in observed)
        {
            var origin = index.TryResolveOrigin(evidence);
            if (!origin.HasValue)
                continue;

            if (originSet.Add(origin.Value))
                origins.Add(origin.Value);
            excludedCandidates.Add(
                new CriticalTriggerOriginExecutionKey(origin.Value, ExecutionKey(evidence))
            );
        }

        if (origins.Count == 0)
            return new CombatImpactCriticalTriggerEvidenceAudit(0, 0)
            {
                InputEventCount = events.Count,
                InputExecutionCount = executions.Count,
                ObservedEvidenceCount = observed.Count,
                WorkUnitCount = index.WorkUnitCount,
            };

        var candidatesByOrigin =
            new Dictionary<CriticalTriggerEvidenceKey, List<IndexedImpactEvent>>();
        for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            index.CountWorkUnit();
            var item = events[eventIndex];
            if (!item.TriggerFrameIndex.HasValue || !CanReceiveCriticalTriggerEvidence(item))
                continue;

            var origin = new CriticalTriggerEvidenceKey(
                item.SourceId,
                item.TriggerFrameIndex.Value
            );
            if (!originSet.Contains(origin))
                continue;
            var executionKey = TryExecutionKey(item);
            if (
                executionKey.HasValue
                && excludedCandidates.Contains(
                    new CriticalTriggerOriginExecutionKey(origin, executionKey.Value)
                )
            )
                continue;

            if (!candidatesByOrigin.TryGetValue(origin, out var candidates))
            {
                candidates = [];
                candidatesByOrigin.Add(origin, candidates);
            }
            candidates.Add(new IndexedImpactEvent(eventIndex, item));
        }

        var attributed = 0;
        foreach (var origin in origins)
        {
            if (!candidatesByOrigin.TryGetValue(origin, out var items))
                continue;
            var alreadyCritical = false;
            foreach (var item in items)
            {
                index.CountWorkUnit();
                if (item.Event.CriticalCount <= 0 && !item.Event.IsCritical)
                    continue;
                alreadyCritical = true;
                break;
            }
            if (alreadyCritical)
            {
                attributed++;
                continue;
            }

            var originating = SelectCriticalTriggerOrigin(items, index);
            events[originating.Index] = events[originating.Index] with
            {
                CriticalCount = 1,
                CriticalOutcomeCount = 1,
                CriticalValue = null,
            };
            attributed++;
        }
        return new CombatImpactCriticalTriggerEvidenceAudit(origins.Count, attributed)
        {
            InputEventCount = events.Count,
            InputExecutionCount = executions.Count,
            ObservedEvidenceCount = observed.Count,
            WorkUnitCount = index.WorkUnitCount,
        };
    }

    private static IReadOnlyList<CriticalTriggerExecutionEvidence> ObserveCriticalTriggerExecutions(
        IReadOnlyList<ProjectedExecution> executions,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var observed = new List<CriticalTriggerExecutionEvidence>();
        var seen = new HashSet<CriticalTriggerExecutionEvidence>();
        foreach (var execution in executions)
        {
            if (
                execution.DirectSourceId is not { Length: > 0 } sourceId
                || execution.TriggerSourceId is not { Length: > 0 } triggerSourceId
                || execution.EffectId is not { Length: > 0 } effectId
                || !entities.TryGetValue(sourceId, out var source)
                || source.CriticalTriggerAbilitiesByEffectId?.TryGetValue(
                    effectId,
                    out var priority
                ) != true
            )
                continue;

            var evidence = new CriticalTriggerExecutionEvidence(
                triggerSourceId,
                execution.FrameIndex,
                priority,
                sourceId,
                effectId,
                execution.ExecutionContextId
            );
            if (seen.Add(evidence))
                observed.Add(evidence);
        }
        return observed;
    }

    private static void NormalizeCriticalTriggerExecutions(
        IList<CombatImpactEvent> events,
        IReadOnlyList<int> listenerEventIndices
    )
    {
        foreach (var index in listenerEventIndices)
        {
            var item = events[index];
            // TTriggerOnCardCritted proves that the triggering activation critted. Its action is a
            // consequence of that crit, not another critical outcome. Native adjustment matching
            // can still inherit a same-frame crit flag, so clear both the result and the recovery
            // candidate before ambiguous-damage recovery runs.
            events[index] = item with
            {
                IsCritical = false,
                CriticalCount = 0,
                CriticalOutcomeCount = 1,
                CriticalValue = null,
                HasCriticalAdjustmentCandidate = false,
            };
        }
    }

    private static bool CanReceiveCriticalTriggerEvidence(CombatImpactEvent item) =>
        item.Surface == CombatImpactEventSurface.AppliedEffect
        && item.IsCritCapable
        && item.OccurrenceBasis == CombatImpactOccurrenceBasis.ExplicitExecution;

    private static IndexedImpactEvent SelectCriticalTriggerOrigin(
        IReadOnlyList<IndexedImpactEvent> candidates,
        CriticalTriggerAttributionIndex index
    )
    {
        var directDamage = default(IndexedImpactEvent);
        var directDamageCount = 0;
        var burn = default(IndexedImpactEvent);
        var burnCount = 0;
        var poison = default(IndexedImpactEvent);
        var poisonCount = 0;
        var healing = default(IndexedImpactEvent);
        var healingCount = 0;
        var shield = default(IndexedImpactEvent);
        var shieldCount = 0;
        var attributeChange = default(IndexedImpactEvent);
        var attributeChangeCount = 0;
        foreach (var candidate in candidates)
        {
            index.CountWorkUnit();
            switch (candidate.Event.Kind)
            {
                case CombatImpactKind.DirectDamage:
                    directDamage = candidate;
                    directDamageCount++;
                    break;
                case CombatImpactKind.Burn:
                    burn = candidate;
                    burnCount++;
                    break;
                case CombatImpactKind.Poison:
                    poison = candidate;
                    poisonCount++;
                    break;
                case CombatImpactKind.Healing:
                    healing = candidate;
                    healingCount++;
                    break;
                case CombatImpactKind.Shield:
                    shield = candidate;
                    shieldCount++;
                    break;
                case CombatImpactKind.AttributeChange:
                    attributeChange = candidate;
                    attributeChangeCount++;
                    break;
            }
        }

        if (directDamageCount == 1)
            return directDamage;
        if (burnCount == 1)
            return burn;
        if (poisonCount == 1)
            return poison;
        if (healingCount == 1)
            return healing;
        if (shieldCount == 1)
            return shield;
        if (attributeChangeCount == 1)
            return attributeChange;
        return candidates[0];
    }

    private static CriticalTriggerExecutionKey ExecutionKey(
        CriticalTriggerExecutionEvidence evidence
    ) =>
        new(
            evidence.FrameIndex,
            evidence.ListenerSourceId,
            evidence.TriggerSourceId,
            evidence.EffectId,
            evidence.ExecutionContextId
        );

    private static CriticalTriggerExecutionKey? TryExecutionKey(ProjectedExecution execution) =>
        execution.DirectSourceId is { Length: > 0 } directSourceId
        && execution.TriggerSourceId is { Length: > 0 } triggerSourceId
        && execution.EffectId is { Length: > 0 } effectId
            ? new CriticalTriggerExecutionKey(
                execution.FrameIndex,
                directSourceId,
                triggerSourceId,
                effectId,
                execution.ExecutionContextId
            )
            : null;

    private static CriticalTriggerExecutionKey? TryExecutionKey(CombatImpactEvent item) =>
        item.TriggerFrameIndex is { } frameIndex
        && item.RawDirectSourceId is { Length: > 0 } directSourceId
        && item.TriggerSourceId is { Length: > 0 } triggerSourceId
        && item.EffectId is { Length: > 0 } effectId
            ? new CriticalTriggerExecutionKey(
                frameIndex,
                directSourceId,
                triggerSourceId,
                effectId,
                item.ExecutionContextId
            )
            : null;

    private static void AttachNativeActivationCriticalCounts(IList<CombatImpactEvent> events)
    {
        var damageByActivation = events
            .Where(IsDirectDamageAppliedEffect)
            .Where(item => item.TriggerFrameIndex.HasValue)
            .GroupBy(item => new NativeActivationKey(
                item.TriggerFrameIndex!.Value,
                item.RawDirectSourceId,
                item.TriggerSourceId
            ))
            .ToDictionary(group => group.Key, group => group.ToArray());

        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            if (
                !CanReceiveNativeActivationCriticality(item)
                || !item.TriggerFrameIndex.HasValue
                || (
                    string.IsNullOrWhiteSpace(item.RawDirectSourceId)
                    && string.IsNullOrWhiteSpace(item.TriggerSourceId)
                )
            )
                continue;

            var key = new NativeActivationKey(
                item.TriggerFrameIndex.Value,
                item.RawDirectSourceId,
                item.TriggerSourceId
            );
            if (
                !damageByActivation.TryGetValue(key, out var damageEvents)
                || damageEvents.Length != 1
                || damageEvents[0].CriticalOutcomeCount != 1
            )
                continue;

            events[index] = item with
            {
                IsCritical = false,
                CriticalCount = damageEvents[0].CriticalCount > 0 ? 1 : 0,
                CriticalOutcomeCount = 1,
                CriticalValue = null,
            };
        }
    }

    private static bool CanReceiveNativeActivationCriticality(CombatImpactEvent item) =>
        item.Surface == CombatImpactEventSurface.AppliedEffect
        && item.IsCritCapable
        && (
            item.Kind == CombatImpactKind.Burn
                && string.Equals(
                    item.NativeAttributeKey,
                    CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
                    StringComparison.Ordinal
                )
            || item.Kind == CombatImpactKind.Poison
                && string.Equals(
                    item.NativeAttributeKey,
                    CombatImpactAggregator.NativeKey(CombatImpactKind.Poison),
                    StringComparison.Ordinal
                )
            || item.Kind == CombatImpactKind.AttributeChange
                && string.Equals(
                    item.NativeAttributeKey,
                    "RegenApplyAmount",
                    StringComparison.Ordinal
                )
        );

    private static bool IsDirectDamageAppliedEffect(CombatImpactEvent item) =>
        item.Surface == CombatImpactEventSurface.AppliedEffect
        && item.Kind == CombatImpactKind.DirectDamage
        && string.Equals(
            item.NativeAttributeKey,
            CombatImpactAggregator.NativeKey(CombatImpactKind.DirectDamage),
            StringComparison.Ordinal
        );

    private static void RecoverAmbiguousDamageCriticals(
        IList<CombatImpactEvent> events,
        IReadOnlyDictionary<string, IReadOnlyList<CombatImpactAuthoritativeMetric>> authoritative
    )
    {
        var candidates = events
            .Select((item, index) => new IndexedImpactEvent(index, item))
            .Where(item => IsDirectDamageAppliedEffect(item.Event))
            .GroupBy(item => new DamageCriticalRecoveryKey(
                item.Event.SourceId,
                item.Event.NativeAttributeKey ?? string.Empty
            ));

        foreach (var group in candidates)
        {
            var indexedEvents = group.ToArray();
            if (!indexedEvents.Any(item => item.Event.HasCriticalAdjustmentCandidate))
                continue;
            if (
                indexedEvents.Any(item =>
                    item.Event.IsCritical
                    || item.Event.CriticalCount > 0
                    || !item.Event.IsCritCapable
                    || item.Event.NonCriticalValue is not > 0
                ) || !authoritative.TryGetValue(group.Key.SourceId, out var sourceMetrics)
            )
                continue;

            var metric = sourceMetrics.SingleOrDefault(item =>
                item.Basis == CombatImpactAuthoritativeBasis.TotalAmount
                && item.Kind == CombatImpactKind.DirectDamage
                && string.Equals(
                    item.NativeAttributeKey,
                    group.Key.NativeAttributeKey,
                    StringComparison.Ordinal
                )
            );
            if (metric == null)
                continue;

            var recovery = ResolveUniqueCriticalRecovery(
                indexedEvents.Select(item => item.Event).ToArray(),
                metric.Value
            );
            if (recovery is not { CriticalCount: > 0 })
                continue;

            foreach (var indexedEvent in indexedEvents)
                events[indexedEvent.Index] = indexedEvent.Event with { CriticalOutcomeCount = 0 };

            var first = indexedEvents[0];
            events[first.Index] = events[first.Index] with
            {
                CriticalCount = recovery.Value.CriticalCount,
                CriticalOutcomeCount = indexedEvents.Length,
                CriticalValue = recovery.Value.CriticalValue,
            };
        }
    }

    private static bool IsCritCapable(
        string? directSourceId,
        string? attributedSourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var capabilitySourceId = IsActivityEntity(directSourceId, entities)
            ? directSourceId
            : attributedSourceId;
        return !string.IsNullOrWhiteSpace(capabilitySourceId)
            && !string.IsNullOrWhiteSpace(effectId)
            && entities.TryGetValue(capabilitySourceId!, out var source)
            && source.CritCapableEffectIds?.Contains(effectId!, StringComparer.Ordinal) == true;
    }

    private static bool HasCriticalHealthAdjustment(
        CombatFrame frame,
        CombatEffectExecuted item,
        CombatImpactKind kind
    )
    {
        if (item.Target is not CombatPlayerTarget playerTarget)
            return false;

        var update =
            playerTarget.Target == Combatant.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
        return update?.HealthAdjustments.Any(adjustment =>
                adjustment.IsCrit && Matches(kind, adjustment)
            ) == true;
    }

    private static CriticalRecoveryResult? ResolveUniqueCriticalRecovery(
        IReadOnlyList<CombatImpactEvent> events,
        long authoritativeTotal
    )
    {
        const int maximumStates = 50_000;
        const int maximumTransitions = 2_000_000;
        long transitions = 0;
        if (authoritativeTotal <= 0)
            return null;

        var states = new Dictionary<long, CriticalRecoveryRange>
        {
            [0] = new CriticalRecoveryRange(0, 0, 0, 0),
        };
        foreach (var item in events)
        {
            var baselineValues = new[] { item.NonCriticalValue, item.AlternateNonCriticalValue }
                .Where(value => value is > 0)
                .Select(value => value!.Value)
                .Distinct()
                .ToArray();
            if (baselineValues.Length == 0)
                return null;

            transitions += (long)states.Count * baselineValues.Length * 2;
            if (transitions > maximumTransitions)
                return null;

            var next = new Dictionary<long, CriticalRecoveryRange>();
            foreach (var (sum, range) in states)
            {
                foreach (var baseline in baselineValues)
                {
                    if (
                        !AddCriticalRecoveryState(
                            next,
                            sum + baseline,
                            range,
                            authoritativeTotal,
                            maximumStates
                        )
                        || !AddCriticalRecoveryState(
                            next,
                            sum + (long)baseline * 2,
                            new CriticalRecoveryRange(
                                range.MinimumCriticalCount + 1,
                                range.MaximumCriticalCount + 1,
                                range.MinimumCriticalValue + (long)baseline * 2,
                                range.MaximumCriticalValue + (long)baseline * 2
                            ),
                            authoritativeTotal,
                            maximumStates
                        )
                    )
                        return null;
                }
            }
            states = next;
        }

        if (
            !states.TryGetValue(authoritativeTotal, out var result)
            || result.MinimumCriticalCount != result.MaximumCriticalCount
            || result.MinimumCriticalCount <= 0
        )
            return null;

        return new CriticalRecoveryResult(
            result.MinimumCriticalCount,
            result.MinimumCriticalValue == result.MaximumCriticalValue
                ? SaturatingInt(result.MinimumCriticalValue)
                : null
        );
    }

    private static bool AddCriticalRecoveryState(
        IDictionary<long, CriticalRecoveryRange> states,
        long total,
        CriticalRecoveryRange candidate,
        long authoritativeTotal,
        int maximumStates
    )
    {
        if (total > authoritativeTotal)
            return true;

        if (states.TryGetValue(total, out var existing))
        {
            states[total] = new CriticalRecoveryRange(
                Math.Min(existing.MinimumCriticalCount, candidate.MinimumCriticalCount),
                Math.Max(existing.MaximumCriticalCount, candidate.MaximumCriticalCount),
                Math.Min(existing.MinimumCriticalValue, candidate.MinimumCriticalValue),
                Math.Max(existing.MaximumCriticalValue, candidate.MaximumCriticalValue)
            );
            return true;
        }

        states[total] = candidate;
        return states.Count <= maximumStates;
    }

    private static PrerequisiteSkillSourceResolution TryResolvePrerequisiteSkillSource(
        ProjectedExecution execution,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        TryResolvePrerequisiteSkillSource(
            execution.DirectSourceId,
            execution.TriggerSourceId,
            execution.EffectId,
            entities
        );

    private static PrerequisiteSkillSourceResolution TryResolvePrerequisiteSkillSource(
        string? directSourceId,
        string? triggerSourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (string.IsNullOrWhiteSpace(effectId))
            return default;
        if (IsActivityEntity(directSourceId, entities))
            return default;

        CombatImpactEntity? implementation = null;
        foreach (var candidateId in new[] { directSourceId, triggerSourceId })
        {
            if (
                string.IsNullOrWhiteSpace(candidateId)
                || !entities.TryGetValue(candidateId!, out var candidate)
                || candidate.TypeLabel is "Item" or "Skill"
                || candidate.PrerequisiteSkillSourceRulesByEffectId?.ContainsKey(effectId!) != true
            )
                continue;

            implementation = candidate;
            break;
        }

        if (
            implementation == null
            || !implementation.PrerequisiteSkillSourceRulesByEffectId!.TryGetValue(
                effectId!,
                out var sourceRule
            )
        )
            return default;

        var skills = FindPrerequisiteSkills(implementation, sourceRule, entities);
        var matchingUseRules =
            implementation
                .UseAttributionRules?.Where(candidate =>
                    string.Equals(candidate.SourceRule.EffectId, effectId, StringComparison.Ordinal)
                )
                .Take(2)
                .ToArray()
            ?? [];
        var useRule = matchingUseRules.Length == 1 ? matchingUseRules[0] : null;
        if (skills.Length == 1)
            return new PrerequisiteSkillSourceResolution(implementation, skills[0], useRule, null);

        return new PrerequisiteSkillSourceResolution(
            implementation,
            null,
            useRule,
            new CombatImpactProjectionDiagnostic(
                skills.Length == 0
                    ? CombatImpactProjectionDiagnosticKind.MissingPrerequisiteSkill
                    : CombatImpactProjectionDiagnosticKind.AmbiguousPrerequisiteSkill,
                implementation.Id,
                effectId!,
                triggerSourceId
            )
        );
    }

    private static CombatImpactEntity[] FindPrerequisiteSkills(
        CombatImpactEntity implementation,
        CombatImpactPrerequisiteSkillSourceRule rule,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        entities
            .Values.Where(candidate =>
                candidate.CombatantId == implementation.CombatantId && rule.Matches(candidate)
            )
            .OrderBy(candidate => candidate.Order)
            .ToArray();

    private static bool TryProjectExplicitUseAttribution(
        ProjectedExecution execution,
        CombatImpactEntity implementation,
        CombatImpactEntity skill,
        CombatImpactUseAttributionRule rule,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out CombatImpactEvent mappedEvent
    )
    {
        mappedEvent = null!;
        if (
            implementation.CombatantId is not { } combatantId
            || implementation.Section != CardSection.Hand
            || !implementation.SocketId.HasValue
            || string.IsNullOrWhiteSpace(execution.TriggerSourceId)
            || !entities.TryGetValue(execution.TriggerSourceId!, out var triggerItem)
            || triggerItem.TypeLabel != "Item"
            || triggerItem.CombatantId != combatantId
            || triggerItem.Section != CardSection.Hand
            || !triggerItem.SocketId.HasValue
            || !rule.ItemCondition.Matches(triggerItem)
            || !SpansOverlap(triggerItem, implementation)
            || execution.TargetId != PlayerId(combatantId)
            || execution.Kind != CombatImpactKind.AttributeChange
            || (
                execution.ActionType != CombatActionType.PlayerModifyAttribute
                && execution.ActionType != CombatActionType.PlayerTempoApply
            )
        )
            return false;

        mappedEvent = CreateUseAttributionEvent(
            implementation,
            skill,
            triggerItem,
            rule,
            execution.FrameIndex,
            CombatImpactOccurrenceBasis.ExplicitExecution
        );
        return true;
    }

    private static CombatImpactEvent CreateUseAttributionEvent(
        CombatImpactEntity implementation,
        CombatImpactEntity skill,
        CombatImpactEntity triggerItem,
        CombatImpactUseAttributionRule rule,
        int? frameIndex,
        CombatImpactOccurrenceBasis occurrenceBasis
    ) =>
        new(
            CombatImpactKind.AttributeChange,
            skill.Id,
            PlayerId(skill.CombatantId!.Value),
            rule.FixedTempoAmount,
            CombatImpactValueUnit.Amount,
            "TempoApplyAmount",
            ValueBasis: CombatImpactValueBasis.ConfiguredActionAmount
        )
        {
            Surface = CombatImpactEventSurface.AppliedEffect,
            OccurrenceBasis = occurrenceBasis,
            RawDirectSourceId = implementation.Id,
            TriggerSourceId = triggerItem.Id,
            TriggerFrameIndex = frameIndex,
            ActivitySourceResolution = CombatImpactActivitySourceResolution.PrerequisiteSkill,
            TriggerScope = CombatImpactTriggerScope.AttributedExternal,
        };

    private static string? ResolveActivitySource(
        string? directSourceId,
        string? triggerSourceId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (IsActivityEntity(directSourceId, entities))
            return directSourceId;
        if (IsActivityEntity(triggerSourceId, entities))
            return triggerSourceId;

        return null;
    }

    private static TriggerProvenance ResolveTriggerProvenance(
        ProjectedExecution execution,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (execution.PrerequisiteSkillSource)
        {
            return new TriggerProvenance(
                CombatImpactActivitySourceResolution.PrerequisiteSkill,
                string.IsNullOrWhiteSpace(execution.TriggerSourceId)
                        ? CombatImpactTriggerScope.NoTriggerEvidence
                    : string.Equals(
                        execution.SourceId,
                        execution.TriggerSourceId,
                        StringComparison.Ordinal
                    )
                        ? CombatImpactTriggerScope.AttributedSelf
                    : CombatImpactTriggerScope.AttributedExternal
            );
        }

        var directIsActivity = IsActivityEntity(execution.DirectSourceId, entities);
        var triggerIsActivity = IsActivityEntity(execution.TriggerSourceId, entities);
        if (!directIsActivity && triggerIsActivity)
        {
            return new TriggerProvenance(
                CombatImpactActivitySourceResolution.TriggerFallback,
                CombatImpactTriggerScope.AttributedViaTriggerFallback
            );
        }

        if (directIsActivity && triggerIsActivity)
        {
            return new TriggerProvenance(
                CombatImpactActivitySourceResolution.Direct,
                string.Equals(
                    execution.DirectSourceId,
                    execution.TriggerSourceId,
                    StringComparison.Ordinal
                )
                    ? CombatImpactTriggerScope.AttributedSelf
                    : CombatImpactTriggerScope.AttributedExternal
            );
        }

        if (directIsActivity && !string.IsNullOrWhiteSpace(execution.TriggerSourceId))
        {
            return new TriggerProvenance(
                CombatImpactActivitySourceResolution.Direct,
                CombatImpactTriggerScope.Unattributed
            );
        }

        return new TriggerProvenance(
            CombatImpactActivitySourceResolution.Direct,
            directIsActivity
                ? CombatImpactTriggerScope.NoTriggerEvidence
                : CombatImpactTriggerScope.NotApplicable
        );
    }

    private static bool TryResolveAbilityAttributeType(
        CombatEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out CombatCardAttribute attributeType
    )
    {
        if (
            TryResolveEffectAttributeType(
                effect.Source,
                effect.TriggerSource,
                effect.EffectId,
                entities,
                static entity => entity.AbilityAttributeTypesByEffectId,
                out attributeType
            )
        )
            return true;
        if (TryResolveAbilityAttributeModifier(effect, entities, out _, out var modifier))
        {
            attributeType = modifier.AttributeType;
            return true;
        }

        attributeType = default;
        return false;
    }

    private static bool TryResolveAbilityAttributeModifier(
        CombatEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out string sourceId,
        out CombatAttributeModifier modifier
    )
    {
        if (
            TryResolveAbilityAttributeModifier(
                effect.Source,
                effect.EffectId,
                entities,
                out sourceId,
                out modifier
            )
        )
            return true;
        if (
            !string.Equals(effect.Source, effect.TriggerSource, StringComparison.Ordinal)
            && TryResolveAbilityAttributeModifier(
                effect.TriggerSource,
                effect.EffectId,
                entities,
                out sourceId,
                out modifier
            )
        )
            return true;

        sourceId = string.Empty;
        modifier = null!;
        return false;
    }

    private static bool TryResolveAbilityAttributeModifier(
        string? candidateSourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out string sourceId,
        out CombatAttributeModifier modifier
    )
    {
        if (
            !string.IsNullOrWhiteSpace(candidateSourceId)
            && !string.IsNullOrWhiteSpace(effectId)
            && entities.TryGetValue(candidateSourceId!, out var source)
            && source.AbilityAttributeModifiersByEffectId is { } modifiers
            && modifiers.TryGetValue(effectId!, out var resolvedModifier)
        )
        {
            sourceId = candidateSourceId!;
            modifier = resolvedModifier;
            return true;
        }

        sourceId = string.Empty;
        modifier = null!;
        return false;
    }

    private static bool TryResolveAuraAttributeType(
        CombatAuraExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out CombatCardAttribute attributeType
    )
    {
        if (TryResolveAuraModifier(effect, entities, out var modifier))
        {
            attributeType = modifier.AttributeType;
            return true;
        }
        return TryResolveEffectAttributeType(
            effect.Source,
            effect.TriggerSource,
            effect.EffectId,
            entities,
            static entity => entity.AuraAttributeTypesByEffectId,
            out attributeType
        );
    }

    private static bool TryResolveAuraModifier(
        CombatAuraExecuted aura,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out CombatAttributeModifier modifier
    )
    {
        foreach (var id in new[] { aura.Source, aura.TriggerSource })
            if (
                id != null
                && entities.TryGetValue(id, out var entity)
                && entity.AuraAttributeModifiersByEffectId?.TryGetValue(
                    aura.EffectId,
                    out modifier!
                ) == true
            )
                return true;
        modifier = null!;
        return false;
    }

    private static bool AuraTouches(CombatAuraExecuted aura, string? targetId) =>
        aura
            .AppliedTo.Concat(aura.RemovedFrom)
            .Any(target =>
                string.Equals(ResolveTargetId(target), targetId, StringComparison.Ordinal)
            );

    private static bool HasAuraClaim(
        CombatFrame frame,
        string? targetId,
        CombatCardAttribute attribute,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        frame
            .Events.OfType<CombatAuraExecuted>()
            .Any(aura =>
                AuraTouches(aura, targetId)
                && (
                    !TryResolveAuraAttributeType(aura, entities, out var expected)
                    || expected == attribute
                )
            );

    private static bool TryResolveEffectAttributeType(
        string? directSourceId,
        string? triggerSourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        Func<CombatImpactEntity, IReadOnlyDictionary<string, CombatCardAttribute>?> selectMappings,
        out CombatCardAttribute attributeType
    )
    {
        if (
            TryResolveEffectAttributeType(
                directSourceId,
                effectId,
                entities,
                selectMappings,
                out attributeType
            )
        )
            return true;
        if (
            !string.Equals(directSourceId, triggerSourceId, StringComparison.Ordinal)
            && TryResolveEffectAttributeType(
                triggerSourceId,
                effectId,
                entities,
                selectMappings,
                out attributeType
            )
        )
            return true;

        attributeType = default;
        return false;
    }

    private static bool TryResolveEffectAttributeType(
        string? sourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        Func<CombatImpactEntity, IReadOnlyDictionary<string, CombatCardAttribute>?> selectMappings,
        out CombatCardAttribute attributeType
    )
    {
        if (
            !string.IsNullOrWhiteSpace(sourceId)
            && !string.IsNullOrWhiteSpace(effectId)
            && entities.TryGetValue(sourceId!, out var source)
            && selectMappings(source)?.TryGetValue(effectId!, out attributeType) == true
        )
            return true;

        attributeType = default;
        return false;
    }

    private static bool IsActivityEntity(
        string? entityId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        !string.IsNullOrWhiteSpace(entityId)
        && entities.TryGetValue(entityId!, out var entity)
        && entity.TypeLabel is "Item" or "Skill";

    private static bool IsDisplayableAttributeChange(ResolvedImpactValue resolved) =>
        resolved.NativeAttributeKey is not ("CardModifyAttribute" or "PlayerModifyAttribute")
        && !resolved.NativeAttributeKey.StartsWith("Custom_", StringComparison.Ordinal);

    private static void AddAuraAttributeEvents(
        CombatTimeline simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        ICollection<CombatImpactEvent> events,
        ICollection<CombatImpactProjectionDiagnostic> diagnostics
    )
    {
        foreach (var frame in simulation.Frames)
        {
            var candidates = new List<AuraAttributeCandidate>();
            var playerCandidates = new List<AuraPlayerAttributeCandidate>();
            foreach (var aura in frame.Events.OfType<CombatAuraExecuted>())
            {
                var directSourceId = aura.Source;
                var triggerSourceId = aura.TriggerSource;
                var sourceId = ResolveActivitySource(directSourceId, triggerSourceId, entities);
                var prerequisite = TryResolvePrerequisiteSkillSource(
                    directSourceId,
                    triggerSourceId,
                    aura.EffectId,
                    entities
                );
                if (prerequisite.Diagnostic != null)
                    diagnostics.Add(prerequisite.Diagnostic);
                if (prerequisite.Skill != null)
                    sourceId = prerequisite.Skill.Id;
                if (sourceId == null)
                    continue;
                var provenance =
                    prerequisite.Skill != null
                        ? new TriggerProvenance(
                            CombatImpactActivitySourceResolution.PrerequisiteSkill,
                            string.IsNullOrWhiteSpace(triggerSourceId)
                                    ? CombatImpactTriggerScope.NoTriggerEvidence
                                : string.Equals(sourceId, triggerSourceId, StringComparison.Ordinal)
                                    ? CombatImpactTriggerScope.AttributedSelf
                                : CombatImpactTriggerScope.AttributedExternal
                        )
                        : ResolveTriggerProvenance(
                            new ProjectedExecution(
                                sourceId,
                                null,
                                null,
                                default,
                                directSourceId,
                                triggerSourceId,
                                0,
                                aura.EffectId,
                                default,
                                aura.ExecutionContextId
                            ),
                            entities
                        );
                var hasExpectedCardAttribute = TryResolveAuraAttributeType(
                    aura,
                    entities,
                    out var expectedCardAttribute
                );
                var isReferenceValuedAura = IsReferenceValuedAuraEffect(aura, entities);

                // Combat teardown removes every surviving aura in one cleanup frame. Those
                // removals are lifecycle bookkeeping, not negative impact caused by the source.
                // Count the grant event itself, regardless of whether the granted value is
                // positive or negative, and ignore RemovedFrom entirely.
                foreach (var target in aura.AppliedTo)
                {
                    if (target is CombatPlayerTarget playerTarget)
                    {
                        var playerId = PlayerId(playerTarget.Target);
                        var playerUpdate =
                            playerTarget.Target == Combatant.Player
                                ? frame.PlayerUpdates
                                : frame.OpponentUpdates;
                        var playerChanges = playerUpdate
                            ?.Attributes.Values.Where(change =>
                                change.Delta != 0
                                && IsDisplayableAuraPlayerAttribute(change.AttributeType)
                            )
                            .ToArray();
                        if (
                            playerChanges?.Length == 1
                            && entities.ContainsKey(playerId)
                            && !frame
                                .Events.OfType<CombatEffectExecuted>()
                                .Any(item =>
                                    string.Equals(
                                        ResolveTargetId(item.Target),
                                        playerId,
                                        StringComparison.Ordinal
                                    )
                                )
                        )
                        {
                            playerCandidates.Add(
                                new AuraPlayerAttributeCandidate(
                                    sourceId,
                                    playerId,
                                    playerChanges[0],
                                    directSourceId,
                                    triggerSourceId,
                                    provenance
                                )
                            );
                        }
                        continue;
                    }

                    if (
                        target is not CombatCardTarget cardTarget
                        || !entities.ContainsKey(cardTarget.Target)
                        || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
                    )
                        continue;
                    if (
                        isReferenceValuedAura
                        && string.Equals(sourceId, cardTarget.Target, StringComparison.Ordinal)
                    )
                        continue;

                    CombatCardAttributeUpdate cardChange;
                    if (hasExpectedCardAttribute)
                    {
                        if (
                            !IsDisplayableAuraAttribute(expectedCardAttribute)
                            || !cardUpdate.Attributes.TryGetValue(
                                expectedCardAttribute,
                                out var expectedChange
                            )
                            || expectedChange.Delta == 0
                            || HasExplicitModifierClaim(
                                frame,
                                cardTarget.Target,
                                expectedCardAttribute,
                                entities
                            )
                        )
                            continue;
                        cardChange = expectedChange;
                    }
                    else
                    {
                        var cardChanges = cardUpdate
                            .Attributes.Values.Where(change =>
                                change.Delta != 0
                                && IsDisplayableAuraAttribute(change.AttributeType)
                            )
                            .ToArray();
                        if (
                            cardChanges.Length != 1
                            || HasExplicitModifierClaim(frame, cardTarget.Target)
                        )
                            continue;
                        cardChange = cardChanges[0];
                    }

                    candidates.Add(
                        new AuraAttributeCandidate(
                            sourceId,
                            cardTarget.Target,
                            cardChange,
                            directSourceId,
                            triggerSourceId,
                            provenance
                        )
                    );
                }
            }

            foreach (
                var candidate in candidates
                    .GroupBy(candidate => (candidate.TargetId, candidate.Change.AttributeType))
                    .Where(group =>
                        group.Count() == 1
                        && frame
                            .Events.OfType<CombatAuraExecuted>()
                            .Count(aura =>
                                AuraTouches(aura, group.Key.TargetId)
                                && (
                                    !TryResolveAuraAttributeType(aura, entities, out var attribute)
                                    || attribute == group.Key.AttributeType
                                )
                            ) == 1
                    )
                    .Select(group => group.Single())
            )
            {
                events.Add(
                    new CombatImpactEvent(
                        CombatImpactKind.AttributeChange,
                        candidate.SourceId,
                        candidate.TargetId,
                        candidate.Change.Delta,
                        UnitFor(candidate.Change.AttributeType.ToString()),
                        candidate.Change.AttributeType.ToString(),
                        ValueBasis: CombatImpactValueBasis.NetFrameDelta
                    )
                    {
                        Surface = CombatImpactEventSurface.CardAttribute,
                        OccurrenceBasis = CombatImpactOccurrenceBasis.ReconstructedTransition,
                        RawDirectSourceId = candidate.DirectSourceId,
                        TriggerSourceId = candidate.TriggerSourceId,
                        ActivitySourceResolution = candidate.Provenance.SourceResolution,
                        TriggerScope = candidate.Provenance.Scope,
                    }
                );
            }

            foreach (
                var candidate in playerCandidates
                    .GroupBy(candidate => (candidate.TargetId, candidate.Change.AttributeType))
                    .Where(group => group.Count() == 1)
                    .Select(group => group.Single())
            )
            {
                events.Add(
                    new CombatImpactEvent(
                        CombatImpactKind.AttributeChange,
                        candidate.SourceId,
                        candidate.TargetId,
                        candidate.Change.Delta,
                        UnitFor(candidate.Change.AttributeType.ToString()),
                        candidate.Change.AttributeType.ToString(),
                        ValueBasis: CombatImpactValueBasis.NetFrameDelta
                    )
                    {
                        Surface = CombatImpactEventSurface.PlayerAttribute,
                        OccurrenceBasis = CombatImpactOccurrenceBasis.ReconstructedTransition,
                        RawDirectSourceId = candidate.DirectSourceId,
                        TriggerSourceId = candidate.TriggerSourceId,
                        ActivitySourceResolution = candidate.Provenance.SourceResolution,
                        TriggerScope = candidate.Provenance.Scope,
                    }
                );
            }
        }
    }

    private static bool IsReferenceValuedAuraEffect(
        CombatAuraExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        HasReferenceValuedAuraEffect(effect.Source, effect.EffectId, entities)
        || HasReferenceValuedAuraEffect(effect.TriggerSource, effect.EffectId, entities);

    private static bool HasReferenceValuedAuraEffect(
        string? sourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        !string.IsNullOrWhiteSpace(sourceId)
        && !string.IsNullOrWhiteSpace(effectId)
        && entities.TryGetValue(sourceId!, out var source)
        && source.ReferenceValuedAuraEffectIds?.Contains(effectId!) == true;

    private static bool HasExplicitModifierClaim(CombatFrame frame, string target) =>
        frame
            .Events.OfType<CombatEffectExecuted>()
            .Any(item =>
                item.ActionType == CombatActionType.CardModifyAttribute
                && item.Target is CombatCardTarget cardTarget
                && cardTarget.Target == target
            );

    private static bool HasExplicitModifierClaim(
        CombatFrame frame,
        string target,
        CombatCardAttribute attributeType,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        frame
            .Events.OfType<CombatEffectExecuted>()
            .Any(item =>
                item.ActionType == CombatActionType.CardModifyAttribute
                && item.Target is CombatCardTarget cardTarget
                && cardTarget.Target == target
                && (
                    !TryResolveAbilityAttributeType(item, entities, out var claimedAttribute)
                    || claimedAttribute == attributeType
                )
            );

    private static bool IsAttributableCardAttribute(CombatCardAttribute attribute) =>
        IsDisplayableAuraAttribute(attribute)
        || attribute.ToString().StartsWith("Custom_", StringComparison.Ordinal);

    private static bool IsDisplayableAuraAttribute(CombatCardAttribute attribute) =>
        attribute
            is CombatCardAttribute.TempoApplyAmount
                or CombatCardAttribute.TempoRemoveAmount
                or CombatCardAttribute.AmmoMax
                or CombatCardAttribute.ReloadAmount
                or CombatCardAttribute.ReloadTargets
                or CombatCardAttribute.CooldownMax
                or CombatCardAttribute.ChargeAmount
                or CombatCardAttribute.ChargeTargets
                or CombatCardAttribute.HasteAmount
                or CombatCardAttribute.HasteTargets
                or CombatCardAttribute.SlowAmount
                or CombatCardAttribute.SlowTargets
                or CombatCardAttribute.FreezeAmount
                or CombatCardAttribute.FreezeTargets
                or CombatCardAttribute.BurnApplyAmount
                or CombatCardAttribute.BurnRemoveAmount
                or CombatCardAttribute.PoisonApplyAmount
                or CombatCardAttribute.PoisonRemoveAmount
                or CombatCardAttribute.Multicast
                or CombatCardAttribute.Lifesteal
                or CombatCardAttribute.CritChance
                or CombatCardAttribute.DamageAmount
                or CombatCardAttribute.DamageCrit
                or CombatCardAttribute.HealAmount
                or CombatCardAttribute.HealCrit
                or CombatCardAttribute.JoyApplyAmount
                or CombatCardAttribute.JoyRemoveAmount
                or CombatCardAttribute.JoyCrit
                or CombatCardAttribute.ShieldApplyAmount
                or CombatCardAttribute.ShieldRemoveAmount
                or CombatCardAttribute.ShieldCrit
                or CombatCardAttribute.ForceUseTargets
                or CombatCardAttribute.EnchantTargets
                or CombatCardAttribute.UpgradeTargets
                or CombatCardAttribute.DisableTargets
                or CombatCardAttribute.RepairTargets
                or CombatCardAttribute.BurnCrit
                or CombatCardAttribute.PoisonCrit
                or CombatCardAttribute.DestroyTargets
                or CombatCardAttribute.RegenApplyAmount
                or CombatCardAttribute.RegenRemoveAmount
                or CombatCardAttribute.RegenCrit
                or CombatCardAttribute.TransformTargets
                or CombatCardAttribute.FlatCooldownReduction
                or CombatCardAttribute.PercentCooldownReduction
                or CombatCardAttribute.EnchantRemoveTargets
                or CombatCardAttribute.FlyingTargets
                or CombatCardAttribute.PercentChargeReduction
                or CombatCardAttribute.PercentHasteReduction
                or CombatCardAttribute.PercentSlowReduction
                or CombatCardAttribute.PercentFreezeReduction
                or CombatCardAttribute.DestroyImmunity
                or CombatCardAttribute.RageApplyAmount
                or CombatCardAttribute.RageRemoveAmount
                or CombatCardAttribute.TempoCost
                or CombatCardAttribute.FlatTempoCostReduction
                or CombatCardAttribute.PercentTempoCostReduction
                or CombatCardAttribute.BuyPrice
                or CombatCardAttribute.SellPrice;

    private static bool IsDisplayableAuraPlayerAttribute(CombatPlayerAttribute attribute) =>
        attribute
            is CombatPlayerAttribute.CritChance
                or CombatPlayerAttribute.DamageCrit
                or CombatPlayerAttribute.JoyCrit
                or CombatPlayerAttribute.HealthMax
                or CombatPlayerAttribute.HealthRegen
                or CombatPlayerAttribute.HealAmount
                or CombatPlayerAttribute.HealCrit
                or CombatPlayerAttribute.ShieldCrit
                or CombatPlayerAttribute.FlatDamageReduction
                or CombatPlayerAttribute.PercentDamageReduction
                or CombatPlayerAttribute.Rage
                or CombatPlayerAttribute.RageMax
                or CombatPlayerAttribute.EnragedDurationMax
                or CombatPlayerAttribute.Tempo
                or CombatPlayerAttribute.TempoGainCooldownMax
                or CombatPlayerAttribute.FlatTempoGainCooldownReduction
                or CombatPlayerAttribute.PercentTempoGainCooldownReduction
                or CombatPlayerAttribute.Experience
                or CombatPlayerAttribute.Gold
                or CombatPlayerAttribute.Income
                or CombatPlayerAttribute.Prestige
                or CombatPlayerAttribute.Level
                or CombatPlayerAttribute.RerollCostModifier;

    public static string PlayerId(Combatant combatant) => $"player:{combatant}";

    private static string? ResolveTargetId(CombatTarget? target) =>
        target switch
        {
            CombatCardTarget card => card.Target,
            CombatPlayerTarget player => PlayerId(player.Target),
            _ => null,
        };

    private static bool IsTransitionUnique(
        CombatFrame frame,
        IReadOnlyList<CombatEffectExecuted> executed,
        CombatEffectExecuted candidate,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (!TryResolveTransitionClaim(candidate.ActionType, out var transition))
            return false;
        var targetId = ResolveTargetId(candidate.Target);
        var claimCount = executed.Count(item =>
            string.Equals(ResolveTargetId(item.Target), targetId, StringComparison.Ordinal)
            && ClaimsTransition(item, transition, entities)
        );
        if (
            transition.Domain == ImpactTransitionDomain.PlayerAttribute
            && transition.Attribute == (int)CombatPlayerAttribute.Tempo
        )
        {
            claimCount += frame.Events.Count(candidate =>
                candidate is CombatCardActionCostSpent costSpent
                && costSpent.PlayerAttributeSpent == CombatPlayerAttribute.Tempo
                && string.Equals(
                    ResolveCardActionCostTargetId(costSpent, entities),
                    targetId,
                    StringComparison.Ordinal
                )
            );
        }

        return claimCount == 1
            && !(
                transition.Domain == ImpactTransitionDomain.PlayerAttribute
                && frame
                    .Events.OfType<CombatAuraExecuted>()
                    .Any(aura => AuraTouches(aura, targetId))
            );
    }

    private static string? ResolveCardActionCostTargetId(
        CombatCardActionCostSpent costSpent,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (
            !entities.TryGetValue(costSpent.ExecutingCard, out var source)
            || !source.CombatantId.HasValue
        )
            return null;

        var targetId = PlayerId(source.CombatantId.Value);
        return entities.ContainsKey(targetId) ? targetId : null;
    }

    private static bool ClaimsTransition(
        CombatActionType action,
        ImpactTransitionClaim transition
    ) => TryResolveTransitionClaim(action, out var candidate) && candidate == transition;

    private static bool TryResolveTransitionClaim(
        CombatActionType action,
        out ImpactTransitionClaim transition
    )
    {
        ImpactTransitionClaim? resolved = action switch
        {
            CombatActionType.PlayerTempoApply or CombatActionType.PlayerTempoRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)CombatPlayerAttribute.Tempo
            ),
            CombatActionType.PlayerDamage => new(
                ImpactTransitionDomain.HealthAdjustment,
                (int)CombatImpactKind.DirectDamage
            ),
            CombatActionType.PlayerHeal => new(
                ImpactTransitionDomain.HealthAdjustment,
                (int)CombatImpactKind.Healing
            ),
            CombatActionType.PlayerShieldApply or CombatActionType.PlayerShieldRemove => new(
                ImpactTransitionDomain.HealthAdjustment,
                (int)CombatImpactKind.Shield
            ),
            CombatActionType.PlayerBurnApply or CombatActionType.PlayerBurnRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)CombatPlayerAttribute.Burn
            ),
            CombatActionType.PlayerPoisonApply or CombatActionType.PlayerPoisonRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)CombatPlayerAttribute.Poison
            ),
            CombatActionType.PlayerRegenApply or CombatActionType.PlayerRegenRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)CombatPlayerAttribute.HealthRegen
            ),
            CombatActionType.PlayerRageApply or CombatActionType.PlayerRageRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)CombatPlayerAttribute.Rage
            ),
            CombatActionType.PlayerMaxHealthIncrease or CombatActionType.PlayerMaxHealthDecrease =>
                new(ImpactTransitionDomain.PlayerAttribute, (int)CombatPlayerAttribute.HealthMax),
            CombatActionType.CardHaste => new(
                ImpactTransitionDomain.CardAttribute,
                (int)CombatCardAttribute.Haste
            ),
            CombatActionType.CardSlow => new(
                ImpactTransitionDomain.CardAttribute,
                (int)CombatCardAttribute.Slow
            ),
            CombatActionType.CardFreeze => new(
                ImpactTransitionDomain.CardAttribute,
                (int)CombatCardAttribute.Freeze
            ),
            CombatActionType.CardReload => new(
                ImpactTransitionDomain.CardAttribute,
                (int)CombatCardAttribute.Ammo
            ),
            _ => null,
        };
        transition = resolved.GetValueOrDefault();
        return resolved.HasValue;
    }

    internal static bool HasExplicitDisplayClassification(CombatActionType action) =>
        TryResolveKind(action, out _) || IsExplicitlyIgnoredAction(action);

    internal static bool TryResolveKind(CombatActionType action, out CombatImpactKind kind)
    {
        CombatImpactKind? resolved = action switch
        {
            CombatActionType.PlayerTempoApply or CombatActionType.PlayerTempoRemove =>
                CombatImpactKind.AttributeChange,
            CombatActionType.PlayerDamage => CombatImpactKind.DirectDamage,
            CombatActionType.PlayerBurnApply => CombatImpactKind.Burn,
            CombatActionType.PlayerPoisonApply => CombatImpactKind.Poison,
            CombatActionType.PlayerHeal => CombatImpactKind.Healing,
            CombatActionType.PlayerShieldApply => CombatImpactKind.Shield,
            CombatActionType.CardCharge => CombatImpactKind.Charge,
            CombatActionType.CardHaste => CombatImpactKind.Haste,
            CombatActionType.CardSlow => CombatImpactKind.Slow,
            CombatActionType.CardFreeze => CombatImpactKind.Freeze,
            CombatActionType.FlyingStart or CombatActionType.FlyingStop => CombatImpactKind.Flying,
            CombatActionType.CardReload => CombatImpactKind.AttributeChange,
            CombatActionType.CardForceUse
            or CombatActionType.CardEnchant
            or CombatActionType.CardEnchantRemove
            or CombatActionType.CardTransform
            or CombatActionType.CardTransformDestroyed
            or CombatActionType.CardUpgrade
            or CombatActionType.CardRepair => CombatImpactKind.AttributeChange,
            CombatActionType.CardModifyAttribute => CombatImpactKind.AttributeChange,
            CombatActionType.PlayerModifyAttribute => CombatImpactKind.AttributeChange,
            CombatActionType.PlayerMaxHealthIncrease => CombatImpactKind.AttributeChange,
            CombatActionType.PlayerMaxHealthDecrease => CombatImpactKind.AttributeChange,
            CombatActionType.PlayerRegenApply => CombatImpactKind.AttributeChange,
            CombatActionType.PlayerRageApply => CombatImpactKind.AttributeChange,
            CombatActionType.PlayerBurnRemove
            or CombatActionType.PlayerPoisonRemove
            or CombatActionType.PlayerRegenRemove
            or CombatActionType.PlayerShieldRemove
            or CombatActionType.PlayerRageRemove => CombatImpactKind.AttributeChange,
            CombatActionType.CardDisable or CombatActionType.CardDestroy =>
                CombatImpactKind.Destroy,
            _ => null,
        };
        kind = resolved.GetValueOrDefault();
        return resolved.HasValue;
    }

    private static bool IsExplicitlyIgnoredAction(CombatActionType action) =>
        IsOptionalGameStateAction(action)
        || action
            is CombatActionType.None
                or CombatActionType.CardAddTags
                or CombatActionType.CardRemoveTags
                or CombatActionType.CardBeginSandstorm
                or CombatActionType.GameDealCards
                or CombatActionType.GameModifyTime
                or CombatActionType.GameScheduleEncounter
                or CombatActionType.GameSpawnCards
                or CombatActionType.GameStartCombat
                or CombatActionType.GameUnscheduleEncounter
                or CombatActionType.PlayerGoldSteal
                or CombatActionType.PlayerJoyApply
                or CombatActionType.PlayerJoyRemove
                or CombatActionType.ExitReplacementSet
                or CombatActionType.CardAddTagsRandom
                or CombatActionType.PlayerPortraitNext
                or CombatActionType.PlayerPortraitReset
                or CombatActionType.GameReroll;

    private static bool IsOptionalGameStateAction(CombatActionType action) =>
        action.ToString()
            is "GameAddToExclusionSet"
                or "GameRemoveFromExclusionSet"
                or "GameSetNextHourSpawnContext";

    private static ResolvedImpactValue ResolveValue(
        CombatFrame frame,
        CombatEffectExecuted item,
        CombatImpactKind kind,
        bool transitionIsUnique,
        IReadOnlyList<CombatEffectExecuted> executed,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes
    )
    {
        if (
            kind == CombatImpactKind.AttributeChange
            && item.ActionType
                is CombatActionType.CardModifyAttribute
                    or CombatActionType.PlayerModifyAttribute
        )
            return ResolveConcreteAttributeTransition(
                frame,
                item,
                kind,
                executed,
                entities,
                cardAttributes
            );

        if (TryResolveCategoricalCardAction(frame, item, kind, out var categoricalAction))
            return categoricalAction;

        if (!transitionIsUnique)
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        if (item.Target is CombatPlayerTarget playerTarget)
        {
            var update =
                playerTarget.Target == Combatant.Player
                    ? frame.PlayerUpdates
                    : frame.OpponentUpdates;
            if (update == null)
                return ResolvedImpactValue.Empty(kind, item.ActionType);

            if (item.ActionType == CombatActionType.PlayerShieldRemove)
            {
                return ResolveShieldRemoval(update)
                    ?? ResolvedImpactValue.Empty(kind, item.ActionType);
            }

            if (
                kind
                is CombatImpactKind.DirectDamage
                    or CombatImpactKind.Healing
                    or CombatImpactKind.Shield
            )
            {
                return ResolveHealthAdjustment(update, kind)
                    ?? ResolvedImpactValue.Empty(kind, item.ActionType);
            }

            CombatPlayerAttribute? expectedAttribute = item.ActionType switch
            {
                CombatActionType.PlayerTempoApply or CombatActionType.PlayerTempoRemove =>
                    CombatPlayerAttribute.Tempo,
                CombatActionType.PlayerBurnApply or CombatActionType.PlayerBurnRemove =>
                    CombatPlayerAttribute.Burn,
                CombatActionType.PlayerPoisonApply or CombatActionType.PlayerPoisonRemove =>
                    CombatPlayerAttribute.Poison,
                CombatActionType.PlayerRegenApply or CombatActionType.PlayerRegenRemove =>
                    CombatPlayerAttribute.HealthRegen,
                CombatActionType.PlayerRageApply or CombatActionType.PlayerRageRemove =>
                    CombatPlayerAttribute.Rage,
                CombatActionType.PlayerMaxHealthIncrease => CombatPlayerAttribute.HealthMax,
                CombatActionType.PlayerMaxHealthDecrease => CombatPlayerAttribute.HealthMax,
                _ => (CombatPlayerAttribute?)null,
            };
            if (
                expectedAttribute.HasValue
                && update.Attributes.TryGetValue(expectedAttribute.Value, out var attribute)
                && MatchesExpectedPlayerDelta(item.ActionType, attribute.Delta)
            )
            {
                return new ResolvedImpactValue(
                    IsPlayerRemovalAction(item.ActionType)
                        ? Math.Abs(attribute.Delta)
                        : attribute.Delta,
                    UnitFor(expectedAttribute.Value.ToString()),
                    NativeKeyForAction(item.ActionType, expectedAttribute.Value.ToString()),
                    false,
                    CombatImpactValueBasis.NetFrameDelta
                );
            }

            return ResolvedImpactValue.Empty(kind, item.ActionType);
        }

        if (
            item.Target is not CombatCardTarget cardTarget
            || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
        )
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        if (kind == CombatImpactKind.Destroy)
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        if (item.ActionType == CombatActionType.CardReload)
        {
            if (
                cardUpdate.Attributes.TryGetValue(CombatCardAttribute.Ammo, out var ammo)
                && ammo.Delta > 0
            )
            {
                return new ResolvedImpactValue(
                    ammo.Delta,
                    CombatImpactValueUnit.Amount,
                    "ReloadAmount",
                    false,
                    CombatImpactValueBasis.NetFrameDelta
                );
            }

            return ResolvedImpactValue.Empty(kind, item.ActionType);
        }

        return ResolvedImpactValue.Empty(kind, item.ActionType);
    }

    private static bool TryResolveCategoricalCardAction(
        CombatFrame frame,
        CombatEffectExecuted item,
        CombatImpactKind kind,
        out ResolvedImpactValue resolved
    )
    {
        var nativeKey = item.ActionType switch
        {
            CombatActionType.CardForceUse => "ForceUseTargets",
            CombatActionType.CardEnchant => ResolveEnchantActionKey(frame, item),
            CombatActionType.CardEnchantRemove => "EnchantRemoveTargets",
            CombatActionType.CardTransform or CombatActionType.CardTransformDestroyed =>
                "TransformTargets",
            CombatActionType.CardUpgrade => "UpgradeTargets",
            CombatActionType.CardRepair => "RepairTargets",
            _ => null,
        };
        if (nativeKey == null)
        {
            resolved = default;
            return false;
        }

        resolved = ResolvedImpactValue.Empty(kind, item.ActionType) with
        {
            NativeAttributeKey = nativeKey,
        };
        return true;
    }

    private static string ResolveEnchantActionKey(CombatFrame frame, CombatEffectExecuted item)
    {
        if (item.Target is not CombatCardTarget cardTarget)
            return "EnchantTargets";

        var targetId = cardTarget.Target;
        var matchingExecutions = frame
            .Events.OfType<CombatEffectExecuted>()
            .Where(candidate =>
                candidate.ActionType == CombatActionType.CardEnchant
                && string.Equals(
                    ResolveTargetId(candidate.Target),
                    targetId,
                    StringComparison.Ordinal
                )
            )
            .Take(2)
            .Count();
        var enchantments = frame
            .Events.OfType<CombatCardEnchanted>()
            .Where(candidate =>
                string.Equals(candidate.InstanceId, targetId, StringComparison.Ordinal)
                && !candidate.IsReverted
                && candidate.EnchantmentType.HasValue
            )
            .Select(candidate => candidate.EnchantmentType!.Value)
            .Distinct()
            .Take(2)
            .ToArray();
        return matchingExecutions == 1 && enchantments.Length == 1
            ? $"EnchantTargets:{enchantments[0]}"
            : "EnchantTargets";
    }

    private static ResolvedImpactValue ResolveConcreteAttributeTransition(
        CombatFrame frame,
        CombatEffectExecuted item,
        CombatImpactKind kind,
        IReadOnlyList<CombatEffectExecuted> executed,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes
    )
    {
        if (item.ActionType == CombatActionType.PlayerModifyAttribute)
        {
            if (item.Target is not CombatPlayerTarget playerTarget)
                return ResolvedImpactValue.Empty(kind, item.ActionType);
            var playerUpdate =
                playerTarget.Target == Combatant.Player
                    ? frame.PlayerUpdates
                    : frame.OpponentUpdates;
            var changes = playerUpdate
                ?.Attributes.Values.Where(update =>
                    update.Delta != 0
                    && IsDisplayableAuraPlayerAttribute(update.AttributeType)
                    && !IsClaimedByAnotherExecution(
                        executed,
                        item,
                        new ImpactTransitionClaim(
                            ImpactTransitionDomain.PlayerAttribute,
                            (int)update.AttributeType
                        )
                    )
                )
                .ToArray();
            if (changes?.Length != 1)
                return ResolvedImpactValue.Empty(kind, item.ActionType);

            var change = changes[0];
            if (
                frame
                    .Events.OfType<CombatAuraExecuted>()
                    .Any(aura => AuraTouches(aura, ResolveTargetId(item.Target)))
            )
                return new ResolvedImpactValue(
                    null,
                    UnitFor(change.AttributeType.ToString()),
                    change.AttributeType.ToString(),
                    false,
                    CombatImpactValueBasis.None
                )
                {
                    Surface = CombatImpactEventSurface.PlayerAttribute,
                    IsUnattributedTransitionClaimant = true,
                    UnattributedTransitionValue = change.Delta,
                    AttributeTransitionNetValue = change.Delta,
                    AttributeTransitionResolution =
                        CombatImpactAttributeTransitionResolution.AuraOverlap,
                    AttributeTransitionFailureReasons =
                    [
                        CombatImpactAttributeTransitionFailureReason.AuraOverlap,
                    ],
                    AttributeTransitionUnresolvedClaimantCount = 1,
                };
            return new ResolvedImpactValue(
                change.Delta,
                UnitFor(change.AttributeType.ToString()),
                change.AttributeType.ToString(),
                false,
                CombatImpactValueBasis.NetFrameDelta
            )
            {
                Surface = CombatImpactEventSurface.PlayerAttribute,
            };
        }

        if (
            item.Target is not CombatCardTarget cardTarget
            || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
        )
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        CombatCardAttributeUpdate cardChange;
        if (TryResolveAbilityAttributeType(item, entities, out var expectedAttribute))
        {
            if (
                !IsAttributableCardAttribute(expectedAttribute)
                || !cardUpdate.Attributes.TryGetValue(expectedAttribute, out var expectedChange)
                || (
                    expectedChange.Delta == 0
                    && !HasAuraClaim(frame, cardTarget.Target, expectedAttribute, entities)
                )
            )
                return ResolvedImpactValue.Empty(kind, item.ActionType);
            cardChange = expectedChange;
        }
        else
        {
            var cardChanges = cardUpdate
                .Attributes.Values.Where(update =>
                    update.Delta != 0 && IsAttributableCardAttribute(update.AttributeType)
                )
                .ToArray();
            if (cardChanges.Length != 1)
                return ResolvedImpactValue.Empty(kind, item.ActionType);
            cardChange = cardChanges[0];
        }
        if (HasAuraClaim(frame, cardTarget.Target, cardChange.AttributeType, entities))
            return ResolveAuraOverlappingCardTransition(
                frame,
                item,
                executed,
                entities,
                cardAttributes,
                cardChange
            );
        if (
            TryResolveConcurrentConfiguredCardAttributeTransition(
                item,
                executed,
                entities,
                cardAttributes,
                cardChange,
                kind,
                out var configuredTransition,
                out var concurrentFailureReasons,
                out var unresolvedClaimantCount,
                out var eventOrderReplayReconciles,
                out var eventOrderReplayIncludesMultiply
            )
        )
            return configuredTransition;
        if (
            IsClaimedByAnotherExecution(
                executed,
                item,
                new ImpactTransitionClaim(
                    ImpactTransitionDomain.CardAttribute,
                    (int)cardChange.AttributeType
                ),
                entities
            )
        )
        {
            if (
                !CanPreserveConcurrentCardAttributeResidual(
                    executed,
                    item,
                    new ImpactTransitionClaim(
                        ImpactTransitionDomain.CardAttribute,
                        (int)cardChange.AttributeType
                    ),
                    entities
                )
            )
                return ResolvedImpactValue.Empty(kind, item.ActionType);

            return new ResolvedImpactValue(
                null,
                UnitFor(cardChange.AttributeType.ToString()),
                cardChange.AttributeType.ToString(),
                false,
                CombatImpactValueBasis.None
            )
            {
                Surface = CombatImpactEventSurface.CardAttribute,
                IsUnattributedTransitionClaimant = true,
                UnattributedTransitionValue = cardChange.Delta,
                AttributeTransitionNetValue = cardChange.Delta,
                AttributeTransitionResolution =
                    CombatImpactAttributeTransitionResolution.ConcurrentResidual,
                AttributeTransitionFailureReasons = concurrentFailureReasons,
                AttributeTransitionUnresolvedClaimantCount = unresolvedClaimantCount,
                AttributeTransitionEventOrderReplayReconciles = eventOrderReplayReconciles,
                AttributeTransitionEventOrderReplayIncludesMultiply =
                    eventOrderReplayIncludesMultiply,
            };
        }

        return new ResolvedImpactValue(
            cardChange.Delta,
            UnitFor(cardChange.AttributeType.ToString()),
            cardChange.AttributeType.ToString(),
            false,
            CombatImpactValueBasis.NetFrameDelta
        )
        {
            Surface = CombatImpactEventSurface.CardAttribute,
            AttributeTransitionNetValue = cardChange.Delta,
            AttributeTransitionResolution =
                CombatImpactAttributeTransitionResolution.SingleClaimantNet,
        };
    }

    private static ResolvedImpactValue ResolveAuraOverlappingCardTransition(
        CombatFrame frame,
        CombatEffectExecuted item,
        IReadOnlyList<CombatEffectExecuted> executed,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes,
        CombatCardAttributeUpdate change
    )
    {
        var targetId = ResolveTargetId(item.Target);
        var claim = new ImpactTransitionClaim(
            ImpactTransitionDomain.CardAttribute,
            (int)change.AttributeType
        );
        var claimants = executed
            .Where(candidate =>
                string.Equals(ResolveTargetId(candidate.Target), targetId, StringComparison.Ordinal)
                && ClaimsTransition(candidate, claim, entities)
            )
            .ToArray();
        // A frame update is a net result, not an execution's operand. Only independent
        // additive actions can be separated from an additive aura without event-local state.
        var additiveAuras = frame
            .Events.OfType<CombatAuraExecuted>()
            .Where(aura =>
                AuraTouches(aura, targetId)
                && (
                    !TryResolveAuraAttributeType(aura, entities, out var attribute)
                    || attribute == change.AttributeType
                )
            )
            .All(aura =>
                TryResolveAuraModifier(aura, entities, out var modifier)
                && modifier.Operation
                    is CombatAttributeOperation.Add
                        or CombatAttributeOperation.Subtract
            );
        if (
            targetId != null
            && entities.TryGetValue(targetId, out var targetEntity)
            && targetEntity.AuraAttributeModifiersByEffectId?.Values.Any(modifier =>
                modifier.AttributeType == change.AttributeType
                && modifier.Operation
                    is not (CombatAttributeOperation.Add or CombatAttributeOperation.Subtract)
            ) == true
        )
            additiveAuras = false;
        int? amount = null;
        if (
            additiveAuras
            && claimants.All(candidate =>
                TryResolveAbilityAttributeModifier(candidate, entities, out _, out var modifier)
                && modifier.Operation
                    is CombatAttributeOperation.Add
                        or CombatAttributeOperation.Subtract
            )
        )
        {
            var stableReference =
                TryResolveAbilityAttributeModifier(
                    item,
                    entities,
                    out var sourceId,
                    out var modifier
                )
                && !(
                    modifier.Value is CombatCardAttributeReference reference
                    && frame.CardUpdates.TryGetValue((sourceId), out var sourceUpdate)
                    && sourceUpdate.Attributes.TryGetValue(
                        reference.AttributeType,
                        out var sourceChange
                    )
                    && sourceChange.Delta != 0
                );
            if (
                stableReference
                && TryResolveConfiguredCardAttributeDelta(
                    item,
                    entities,
                    cardAttributes,
                    change.AttributeType,
                    out var delta,
                    out _
                )
            )
                amount = delta;
        }
        return new ResolvedImpactValue(
            amount,
            UnitFor(change.AttributeType.ToString()),
            change.AttributeType.ToString(),
            false,
            amount.HasValue
                ? CombatImpactValueBasis.ConfiguredActionAmount
                : CombatImpactValueBasis.None
        )
        {
            Surface = CombatImpactEventSurface.CardAttribute,
            IsUnattributedTransitionClaimant = !amount.HasValue,
            UnattributedTransitionValue = change.Delta,
            AttributeTransitionNetValue = change.Delta,
            AttributeTransitionResolution = CombatImpactAttributeTransitionResolution.AuraOverlap,
            AttributeTransitionFailureReasons =
            [
                CombatImpactAttributeTransitionFailureReason.AuraOverlap,
            ],
            AttributeTransitionUnresolvedClaimantCount = amount.HasValue ? 0 : 1,
        };
    }

    private static bool TryResolveConcurrentConfiguredCardAttributeTransition(
        CombatEffectExecuted item,
        IReadOnlyList<CombatEffectExecuted> executed,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes,
        CombatCardAttributeUpdate cardChange,
        CombatImpactKind kind,
        out ResolvedImpactValue resolved,
        out IReadOnlyList<CombatImpactAttributeTransitionFailureReason> failureReasons,
        out int unresolvedClaimantCount,
        out bool eventOrderReplayReconciles,
        out bool eventOrderReplayIncludesMultiply
    )
    {
        resolved = default;
        failureReasons = [];
        unresolvedClaimantCount = 0;
        eventOrderReplayReconciles = false;
        eventOrderReplayIncludesMultiply = false;
        var targetId = ResolveTargetId(item.Target);
        var transition = new ImpactTransitionClaim(
            ImpactTransitionDomain.CardAttribute,
            (int)cardChange.AttributeType
        );
        var claimants = executed
            .Where(candidate =>
                string.Equals(ResolveTargetId(candidate.Target), targetId, StringComparison.Ordinal)
                && ClaimsTransition(candidate, transition, entities)
            )
            .ToArray();
        if (claimants.Length <= 1)
            return false;

        long configuredTotal = 0;
        int? itemDelta = null;
        CombatEffectExecuted? unknownClaimant = null;
        var unknownFailureReason = default(CombatImpactAttributeTransitionFailureReason);
        var failures = new HashSet<CombatImpactAttributeTransitionFailureReason>();
        foreach (var claimant in claimants)
        {
            if (
                !TryResolveConfiguredCardAttributeDelta(
                    claimant,
                    entities,
                    cardAttributes,
                    cardChange.AttributeType,
                    out var configuredDelta,
                    out var failureReason
                )
            )
            {
                failures.Add(failureReason);
                unresolvedClaimantCount++;
                if (unresolvedClaimantCount == 1)
                {
                    unknownClaimant = claimant;
                    unknownFailureReason = failureReason;
                }
                else
                {
                    unknownClaimant = null;
                }
                continue;
            }

            configuredTotal += configuredDelta;
            if (ReferenceEquals(claimant, item))
                itemDelta = configuredDelta;
        }

        if (
            unresolvedClaimantCount == 1
            && unknownClaimant != null
            && unknownFailureReason
                != CombatImpactAttributeTransitionFailureReason.ModifierUnavailable
        )
        {
            var solvedDelta = (long)cardChange.Delta - configuredTotal;
            if (
                solvedDelta is >= int.MinValue and <= int.MaxValue
                && IsPlausibleSingleUnknownDelta(
                    unknownClaimant,
                    entities,
                    cardChange.AttributeType,
                    (int)solvedDelta
                )
            )
            {
                if (ReferenceEquals(unknownClaimant, item))
                    itemDelta = (int)solvedDelta;
                if (itemDelta.HasValue)
                {
                    failureReasons = failures.OrderBy(reason => reason).ToArray();
                    unresolvedClaimantCount = 0;
                    resolved = new ResolvedImpactValue(
                        itemDelta.Value,
                        UnitFor(cardChange.AttributeType.ToString()),
                        cardChange.AttributeType.ToString(),
                        false,
                        CombatImpactValueBasis.ConfiguredActionAmount
                    )
                    {
                        Surface = CombatImpactEventSurface.CardAttribute,
                        AttributeTransitionNetValue = cardChange.Delta,
                        AttributeTransitionResolution =
                            CombatImpactAttributeTransitionResolution.ConcurrentSingleUnknownSolved,
                        AttributeTransitionFailureReasons = failureReasons,
                    };
                    return true;
                }
            }
        }

        if (failures.Count > 0)
        {
            eventOrderReplayReconciles = TryReplayConfiguredCardAttributeTransitionInEventOrder(
                claimants,
                entities,
                cardAttributes,
                targetId,
                cardChange,
                out eventOrderReplayIncludesMultiply
            );
            failureReasons = failures.OrderBy(reason => reason).ToArray();
            return false;
        }
        if (!itemDelta.HasValue)
        {
            failureReasons =
            [
                CombatImpactAttributeTransitionFailureReason.ClaimantIdentityMismatch,
            ];
            return false;
        }
        if (configuredTotal != cardChange.Delta)
        {
            eventOrderReplayReconciles = TryReplayConfiguredCardAttributeTransitionInEventOrder(
                claimants,
                entities,
                cardAttributes,
                targetId,
                cardChange,
                out eventOrderReplayIncludesMultiply
            );
            failureReasons = [CombatImpactAttributeTransitionFailureReason.ConfiguredSumMismatch];
            return false;
        }

        resolved = new ResolvedImpactValue(
            itemDelta.Value,
            UnitFor(cardChange.AttributeType.ToString()),
            cardChange.AttributeType.ToString(),
            false,
            CombatImpactValueBasis.ConfiguredActionAmount
        )
        {
            Surface = CombatImpactEventSurface.CardAttribute,
            AttributeTransitionNetValue = cardChange.Delta,
            AttributeTransitionResolution =
                CombatImpactAttributeTransitionResolution.ConcurrentConfiguredExact,
        };
        return true;
    }

    private static bool TryReplayConfiguredCardAttributeTransitionInEventOrder(
        IReadOnlyList<CombatEffectExecuted> claimants,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes,
        string? targetId,
        CombatCardAttributeUpdate cardChange,
        out bool includesMultiply
    )
    {
        includesMultiply = false;
        if (string.IsNullOrWhiteSpace(targetId))
            return false;

        long runningValue = cardChange.PreviousValue;
        foreach (var claimant in claimants)
        {
            if (
                !TryResolveAbilityAttributeModifier(
                    claimant,
                    entities,
                    out var modifierSourceId,
                    out var modifier
                )
                || modifier.AttributeType != cardChange.AttributeType
                || !TryResolveEventOrderReplayValue(
                    modifier.Value,
                    modifierSourceId,
                    targetId!,
                    cardChange.AttributeType,
                    runningValue,
                    cardAttributes,
                    out var operand
                )
            )
                return false;

            runningValue = modifier.Operation switch
            {
                CombatAttributeOperation.Add => runningValue + operand,
                CombatAttributeOperation.Subtract => runningValue - operand,
                CombatAttributeOperation.Multiply => runningValue * operand,
                _ => long.MinValue,
            };
            if (modifier.Operation == CombatAttributeOperation.Multiply)
                includesMultiply = true;
            if (runningValue is < int.MinValue or > int.MaxValue)
                return false;
        }

        return runningValue == cardChange.CurrentValue;
    }

    private static bool TryResolveEventOrderReplayValue(
        CombatValue value,
        string sourceId,
        string targetId,
        CombatCardAttribute targetAttribute,
        long runningTargetValue,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes,
        out int configuredValue
    )
    {
        if (value is CombatFixedValue fixedValue)
            return TryConvertExactInt(fixedValue.Value, out configuredValue);
        if (value is not CombatCardAttributeReference reference)
        {
            configuredValue = 0;
            return false;
        }
        if (!reference.TargetsSelf)
        {
            configuredValue = 0;
            return false;
        }

        long rawValue;
        if (
            string.Equals(sourceId, targetId, StringComparison.Ordinal)
            && reference.AttributeType == targetAttribute
        )
        {
            rawValue = runningTargetValue;
        }
        else if (
            !cardAttributes.TryGetValue(sourceId, out var sourceAttributes)
            || !sourceAttributes.TryGetValue(reference.AttributeType, out var sourceValue)
        )
        {
            configuredValue = 0;
            return false;
        }
        else
        {
            rawValue = sourceValue;
        }
        if (rawValue is < int.MinValue or > int.MaxValue)
        {
            configuredValue = 0;
            return false;
        }
        if (!TryApplyStaticModifier((int)rawValue, reference.Modifier, out var modifiedValue))
        {
            configuredValue = 0;
            return false;
        }
        return TryConvertExactInt(modifiedValue, out configuredValue);
    }

    private static bool IsPlausibleSingleUnknownDelta(
        CombatEffectExecuted claimant,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        CombatCardAttribute expectedAttribute,
        int solvedDelta
    )
    {
        if (
            !TryResolveAbilityAttributeModifier(claimant, entities, out _, out var modifier)
            || modifier.AttributeType != expectedAttribute
        )
            return false;
        if (
            modifier.Value is not CombatRangeValue range
            || modifier.Operation
                is not (CombatAttributeOperation.Add or CombatAttributeOperation.Subtract)
        )
            return true;
        if (
            !TryApplyStaticModifier(range.MinValue, range.Modifier, out var firstBound)
            || !TryApplyStaticModifier(range.MaxValue, range.Modifier, out var secondBound)
        )
            return false;

        var minimum = Math.Min(firstBound, secondBound);
        var maximum = Math.Max(firstBound, secondBound);
        if (modifier.Operation == CombatAttributeOperation.Subtract)
            (minimum, maximum) = (-maximum, -minimum);
        return solvedDelta >= minimum - 0.0001f && solvedDelta <= maximum + 0.0001f;
    }

    private static bool CanPreserveConcurrentCardAttributeResidual(
        IReadOnlyList<CombatEffectExecuted> executed,
        CombatEffectExecuted item,
        ImpactTransitionClaim transition,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var targetId = ResolveTargetId(item.Target);
        var claimants = executed
            .Where(candidate =>
                string.Equals(ResolveTargetId(candidate.Target), targetId, StringComparison.Ordinal)
                && ClaimsTransition(candidate, transition, entities)
            )
            .ToArray();
        return claimants.Length > 1
            && claimants.All(candidate =>
                candidate.ActionType == CombatActionType.CardModifyAttribute
            );
    }

    private static bool TryResolveConfiguredCardAttributeDelta(
        CombatEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes,
        CombatCardAttribute expectedAttribute,
        out int delta,
        out CombatImpactAttributeTransitionFailureReason failureReason
    )
    {
        delta = 0;
        failureReason = default;
        if (effect.ActionType != CombatActionType.CardModifyAttribute)
        {
            failureReason = CombatImpactAttributeTransitionFailureReason.UnsupportedOperation;
            return false;
        }
        if (
            !TryResolveAbilityAttributeModifier(
                effect,
                entities,
                out var modifierSourceId,
                out var modifier
            )
        )
        {
            failureReason = CombatImpactAttributeTransitionFailureReason.ModifierUnavailable;
            return false;
        }
        if (modifier.AttributeType != expectedAttribute)
        {
            failureReason = CombatImpactAttributeTransitionFailureReason.AttributeMismatch;
            return false;
        }
        if (
            modifier.Operation
            is not (CombatAttributeOperation.Add or CombatAttributeOperation.Subtract)
        )
        {
            failureReason = modifier.Operation switch
            {
                CombatAttributeOperation.Multiply =>
                    CombatImpactAttributeTransitionFailureReason.MultiplyOperation,
                CombatAttributeOperation.AdditiveMultiply =>
                    CombatImpactAttributeTransitionFailureReason.AdditiveMultiplyOperation,
                _ => CombatImpactAttributeTransitionFailureReason.UnsupportedOperation,
            };
            return false;
        }
        if (
            !TryResolveConfiguredValue(
                modifier.Value,
                modifierSourceId,
                cardAttributes,
                out var configuredValue,
                out failureReason
            )
        )
            return false;

        delta =
            modifier.Operation == CombatAttributeOperation.Subtract
                ? -configuredValue
                : configuredValue;
        return true;
    }

    private static bool TryResolveConfiguredValue(
        CombatValue value,
        string sourceId,
        IReadOnlyDictionary<string, Dictionary<CombatCardAttribute, int>> cardAttributes,
        out int configuredValue,
        out CombatImpactAttributeTransitionFailureReason failureReason
    )
    {
        failureReason = default;
        switch (value)
        {
            case CombatFixedValue fixedValue:
                if (TryConvertExactInt(fixedValue.Value, out configuredValue))
                    return true;
                failureReason =
                    CombatImpactAttributeTransitionFailureReason.NonIntegralConfiguredValue;
                return false;
            case CombatCardAttributeReference reference:
                if (!reference.TargetsSelf)
                {
                    configuredValue = 0;
                    failureReason =
                        CombatImpactAttributeTransitionFailureReason.NonSelfCardReference;
                    return false;
                }
                if (
                    !cardAttributes.TryGetValue(sourceId, out var attributes)
                    || !attributes.TryGetValue(reference.AttributeType, out var attributeValue)
                )
                {
                    configuredValue = 0;
                    failureReason =
                        CombatImpactAttributeTransitionFailureReason.SourceAttributeUnavailable;
                    return false;
                }
                if (
                    !TryApplyStaticModifier(
                        attributeValue,
                        reference.Modifier,
                        out var modifiedValue
                    )
                )
                {
                    configuredValue = 0;
                    failureReason =
                        CombatImpactAttributeTransitionFailureReason.DynamicReferenceModifier;
                    return false;
                }
                if (TryConvertExactInt(modifiedValue, out configuredValue))
                    return true;
                failureReason =
                    CombatImpactAttributeTransitionFailureReason.NonIntegralConfiguredValue;
                return false;
            case CombatRangeValue:
                configuredValue = 0;
                failureReason = CombatImpactAttributeTransitionFailureReason.RangeValue;
                return false;
            case CombatOpaqueReference { Kind: CombatOpaqueReferenceKind.CardAttributeUnscaled }:
                configuredValue = 0;
                failureReason = CombatImpactAttributeTransitionFailureReason.CardAttributeUnscaled;
                return false;
            case CombatOpaqueReference { Kind: CombatOpaqueReferenceKind.AttributeChange }:
                configuredValue = 0;
                failureReason =
                    CombatImpactAttributeTransitionFailureReason.AttributeChangeReference;
                return false;
            case CombatOpaqueReference { Kind: CombatOpaqueReferenceKind.PlayerAttribute }:
                configuredValue = 0;
                failureReason =
                    CombatImpactAttributeTransitionFailureReason.PlayerAttributeReference;
                return false;
            case CombatOpaqueReference { Kind: CombatOpaqueReferenceKind.CardCount }:
                configuredValue = 0;
                failureReason = CombatImpactAttributeTransitionFailureReason.CardCountReference;
                return false;
            default:
                configuredValue = 0;
                failureReason = CombatImpactAttributeTransitionFailureReason.UnsupportedValueType;
                return false;
        }
    }

    private static bool TryApplyStaticModifier(
        float originalValue,
        CombatValueModifier? modifier,
        out float modifiedValue
    )
    {
        if (modifier == null)
        {
            modifiedValue = originalValue;
            return true;
        }
        if (modifier.Value is not CombatFixedValue operand)
        {
            modifiedValue = 0;
            return false;
        }

        // The fixed operand is the only reference modifier we can evaluate without live game
        // context. Its rounding and divide-by-zero semantics are the game's own.
        modifiedValue = modifier.GetModifiedValue(originalValue, operand.Value);
        return !float.IsNaN(modifiedValue) && !float.IsInfinity(modifiedValue);
    }

    private static bool TryConvertExactInt(float value, out int converted)
    {
        converted = 0;
        if (
            float.IsNaN(value)
            || float.IsInfinity(value)
            || value < int.MinValue
            || value > int.MaxValue
        )
            return false;

        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (Math.Abs(value - rounded) > 0.0001d)
            return false;

        converted = (int)rounded;
        return true;
    }

    private static bool IsClaimedByAnotherExecution(
        IReadOnlyList<CombatEffectExecuted> executed,
        CombatEffectExecuted item,
        ImpactTransitionClaim transition,
        IReadOnlyDictionary<string, CombatImpactEntity>? entities = null
    )
    {
        var targetId = ResolveTargetId(item.Target);
        return executed.Any(candidate =>
            !ReferenceEquals(candidate, item)
            && string.Equals(ResolveTargetId(candidate.Target), targetId, StringComparison.Ordinal)
            && ClaimsTransition(candidate, transition, entities)
        );
    }

    private static bool ClaimsTransition(
        CombatEffectExecuted effect,
        ImpactTransitionClaim transition,
        IReadOnlyDictionary<string, CombatImpactEntity>? entities
    )
    {
        if (
            entities != null
            && effect.ActionType == CombatActionType.CardModifyAttribute
            && transition.Domain == ImpactTransitionDomain.CardAttribute
        )
        {
            return !TryResolveAbilityAttributeType(effect, entities, out var attributeType)
                || (int)attributeType == transition.Attribute;
        }

        // Generic player modifiers are competing writers too. Without an effect-local
        // attribute mapping, none may consume the same net player delta independently.
        if (
            effect.ActionType == CombatActionType.PlayerModifyAttribute
            && transition.Domain == ImpactTransitionDomain.PlayerAttribute
        )
            return true;
        return ClaimsTransition(effect.ActionType, transition);
    }

    private static ResolvedImpactValue? ResolveHealthAdjustment(
        CombatPlayerUpdate update,
        CombatImpactKind kind
    )
    {
        var matches = update
            .HealthAdjustments.Where(adjustment => Matches(kind, adjustment))
            .ToArray();
        return ResolveHealthAdjustments(matches, kind);
    }

    private static bool TryResolveConcurrentHealthAdjustment(
        CombatFrame frame,
        CombatEffectExecuted item,
        CombatImpactKind kind,
        IReadOnlyList<CombatEffectExecuted> executed,
        out ResolvedImpactValue resolved
    )
    {
        resolved = default;
        if (
            kind
                is not (
                    CombatImpactKind.DirectDamage
                    or CombatImpactKind.Healing
                    or CombatImpactKind.Shield
                )
            || item.Target is not CombatPlayerTarget playerTarget
        )
            return false;

        var targetId = ResolveTargetId(item.Target);
        var claimants = executed
            .Where(candidate =>
                string.Equals(ResolveTargetId(candidate.Target), targetId, StringComparison.Ordinal)
                && TryResolveKind(candidate.ActionType, out var candidateKind)
                && candidateKind == kind
            )
            .ToArray();
        if (claimants.Length <= 1)
            return false;

        var claimantIndex = Array.FindIndex(
            claimants,
            candidate => ReferenceEquals(candidate, item)
        );
        if (claimantIndex < 0)
            return false;

        var update =
            playerTarget.Target == Combatant.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
        var adjustments = update
            ?.HealthAdjustments.Where(adjustment => Matches(kind, adjustment))
            .ToArray();
        if (adjustments is not { Length: > 0 })
            return false;

        if (kind != CombatImpactKind.DirectDamage)
        {
            if (adjustments.Length != claimants.Length)
                return false;
            var healthAdjustment = ResolveHealthAdjustments([adjustments[claimantIndex]], kind);
            if (!healthAdjustment.HasValue)
                return false;
            resolved = healthAdjustment.Value;
            return true;
        }

        var candidates = ResolveConcurrentDamageCandidates(
            adjustments,
            claimants.Length,
            claimantIndex
        );
        if (candidates.Count == 0)
            return false;

        var isCritical = candidates[0].IsCritical;
        if (candidates.Any(candidate => candidate.IsCritical != isCritical))
            return false;

        var values = candidates.Select(candidate => candidate.Value).Distinct().ToArray();
        var value = values.Length == 1 ? values[0] : null;
        resolved = new ResolvedImpactValue(
            value,
            CombatImpactValueUnit.Amount,
            CombatImpactAggregator.NativeKey(kind),
            isCritical,
            value.HasValue ? CombatImpactValueBasis.ExactAdjustment : CombatImpactValueBasis.None
        )
        {
            CriticalCount = isCritical ? 1 : 0,
            CriticalOutcomeCount = 1,
            CriticalValue = isCritical ? value : null,
            HasCriticalAdjustmentCandidate = isCritical,
        };
        return true;
    }

    private static IReadOnlyList<ResolvedImpactValue> ResolveConcurrentDamageCandidates(
        IReadOnlyList<CombatHealthAdjustment> adjustments,
        int effectCount,
        int requestedEffectIndex
    )
    {
        const int maximumCandidates = 1_024;
        var candidates = new List<ResolvedImpactValue>();
        var partition = new List<IReadOnlyList<CombatHealthAdjustment>>(effectCount);
        var wasTruncated = false;

        void Visit(int adjustmentIndex)
        {
            if (candidates.Count >= maximumCandidates)
            {
                wasTruncated = true;
                return;
            }
            if (partition.Count == effectCount)
            {
                if (adjustmentIndex != adjustments.Count)
                    return;
                var candidate = ResolveHealthAdjustments(
                    partition[requestedEffectIndex],
                    CombatImpactKind.DirectDamage
                );
                if (candidate.HasValue)
                    candidates.Add(candidate.Value);
                return;
            }

            var remainingEffects = effectCount - partition.Count;
            var remainingAdjustments = adjustments.Count - adjustmentIndex;
            if (
                adjustmentIndex >= adjustments.Count
                || remainingAdjustments < remainingEffects
                || remainingAdjustments > remainingEffects * 2
            )
                return;

            partition.Add([adjustments[adjustmentIndex]]);
            Visit(adjustmentIndex + 1);
            partition.RemoveAt(partition.Count - 1);

            if (
                adjustmentIndex + 1 >= adjustments.Count
                || !CanBelongToSameDamageEffect(
                    adjustments[adjustmentIndex],
                    adjustments[adjustmentIndex + 1]
                )
            )
                return;

            partition.Add([adjustments[adjustmentIndex], adjustments[adjustmentIndex + 1]]);
            Visit(adjustmentIndex + 2);
            partition.RemoveAt(partition.Count - 1);
        }

        Visit(0);
        return wasTruncated ? [] : candidates;
    }

    private static bool CanBelongToSameDamageEffect(
        CombatHealthAdjustment first,
        CombatHealthAdjustment second
    ) =>
        first.AttributeChanged == CombatHealthChange.Health
        && second.AttributeChanged == CombatHealthChange.Shield
        && first.IsCrit == second.IsCrit;

    private static ResolvedImpactValue? ResolveHealthAdjustments(
        IReadOnlyList<CombatHealthAdjustment> matches,
        CombatImpactKind kind
    )
    {
        if (matches.Count == 0)
            return null;

        long aggregate = 0;
        foreach (var adjustment in matches)
        {
            aggregate +=
                kind == CombatImpactKind.DirectDamage
                    ? Math.Abs((long)adjustment.Amount)
                    : adjustment.Amount;
        }
        var value = SaturatingInt(aggregate);
        if (value <= 0)
            return null;

        return new ResolvedImpactValue(
            value,
            CombatImpactValueUnit.Amount,
            CombatImpactAggregator.NativeKey(kind),
            matches.All(adjustment => adjustment.IsCrit),
            CombatImpactValueBasis.ExactAdjustment
        )
        {
            CriticalCount = matches.Any(adjustment => adjustment.IsCrit) ? 1 : 0,
            CriticalOutcomeCount = 1,
            CriticalValue = SaturatingInt(
                matches
                    .Where(adjustment => adjustment.IsCrit)
                    .Sum(adjustment =>
                        kind == CombatImpactKind.DirectDamage
                            ? Math.Abs((long)adjustment.Amount)
                            : adjustment.Amount
                    )
            ),
        };
    }

    private static ResolvedImpactValue? ResolveShieldRemoval(CombatPlayerUpdate update)
    {
        var matches = update
            .HealthAdjustments.Where(adjustment =>
                adjustment.DamageType == CombatDamageType.Shield
                && adjustment.AttributeChanged == CombatHealthChange.Shield
                && adjustment.Amount < 0
            )
            .ToArray();
        if (matches.Length == 0)
            return null;

        var removed = SaturatingInt(matches.Sum(adjustment => Math.Abs((long)adjustment.Amount)));
        return removed > 0
            ? new ResolvedImpactValue(
                removed,
                CombatImpactValueUnit.Amount,
                "ShieldRemoveAmount",
                false,
                CombatImpactValueBasis.ExactAdjustment
            )
            : null;
    }

    private static bool MatchesExpectedPlayerDelta(CombatActionType action, int delta)
    {
        var isDecrease =
            action == CombatActionType.PlayerMaxHealthDecrease || IsPlayerRemovalAction(action);
        return isDecrease ? delta < 0 : delta > 0;
    }

    private static bool IsPlayerRemovalAction(CombatActionType action) =>
        action
            is CombatActionType.PlayerTempoRemove
                or CombatActionType.PlayerBurnRemove
                or CombatActionType.PlayerPoisonRemove
                or CombatActionType.PlayerRegenRemove
                or CombatActionType.PlayerShieldRemove
                or CombatActionType.PlayerRageRemove;

    private static int SaturatingInt(long value) =>
        value > int.MaxValue ? int.MaxValue
        : value < int.MinValue ? int.MinValue
        : (int)value;

    private static bool Matches(CombatImpactKind kind, CombatHealthAdjustment adjustment) =>
        kind switch
        {
            CombatImpactKind.DirectDamage => adjustment.DamageType == CombatDamageType.Damage
                && adjustment.Amount < 0,
            CombatImpactKind.Healing => adjustment.DamageType == CombatDamageType.Heal
                && adjustment.AttributeChanged == CombatHealthChange.Health
                && adjustment.Amount > 0,
            CombatImpactKind.Shield => adjustment.DamageType == CombatDamageType.Shield
                && adjustment.AttributeChanged == CombatHealthChange.Shield
                && adjustment.Amount > 0,
            _ => false,
        };

    private static string NativeKeyForAction(CombatActionType action, string fallback)
    {
        return action switch
        {
            CombatActionType.PlayerTempoApply => "TempoApplyAmount",
            CombatActionType.PlayerTempoRemove => "TempoRemoveAmount",
            CombatActionType.CardReload => "ReloadAmount",
            CombatActionType.CardForceUse => "ForceUseTargets",
            CombatActionType.CardEnchant => "EnchantTargets",
            CombatActionType.CardEnchantRemove => "EnchantRemoveTargets",
            CombatActionType.CardTransform or CombatActionType.CardTransformDestroyed =>
                "TransformTargets",
            CombatActionType.CardUpgrade => "UpgradeTargets",
            CombatActionType.CardRepair => "RepairTargets",
            CombatActionType.CardModifyAttribute => "CardModifyAttribute",
            CombatActionType.PlayerModifyAttribute => "PlayerModifyAttribute",
            CombatActionType.PlayerBurnApply => CombatImpactAggregator.NativeKey(
                CombatImpactKind.Burn
            ),
            CombatActionType.PlayerPoisonApply => CombatImpactAggregator.NativeKey(
                CombatImpactKind.Poison
            ),
            CombatActionType.PlayerBurnRemove => "BurnRemoveAmount",
            CombatActionType.PlayerPoisonRemove => "PoisonRemoveAmount",
            CombatActionType.PlayerRegenApply => "RegenApplyAmount",
            CombatActionType.PlayerRegenRemove => "RegenRemoveAmount",
            CombatActionType.PlayerRageApply => "RageApplyAmount",
            CombatActionType.PlayerRageRemove => "RageRemoveAmount",
            CombatActionType.PlayerShieldRemove => "ShieldRemoveAmount",
            CombatActionType.PlayerMaxHealthIncrease => "HealthMaxIncrease",
            CombatActionType.PlayerMaxHealthDecrease => "HealthMaxDecrease",
            _ => fallback,
        };
    }

    private static CombatImpactValueUnit UnitFor(string nativeAttributeKey) =>
        nativeAttributeKey == "Lifesteal"
        || nativeAttributeKey.Contains("Percent", StringComparison.Ordinal)
        || nativeAttributeKey.Contains("CritChance", StringComparison.Ordinal)
            ? CombatImpactValueUnit.PercentagePoints
        : nativeAttributeKey
            is "Cooldown"
                or "CooldownMax"
                or "ChargeAmount"
                or "Haste"
                or "HasteAmount"
                or "Slow"
                or "SlowAmount"
                or "Freeze"
                or "FreezeAmount"
                or "FlatCooldownReduction"
                or "TempoGainCooldownMax"
                or "FlatTempoGainCooldownReduction"
            ? CombatImpactValueUnit.Milliseconds
        : CombatImpactValueUnit.Amount;

    private static void AddAmountMetric(
        IReadOnlyDictionary<CombatCardStat, int> stats,
        CombatCardStat stat,
        CombatImpactKind kind,
        string nativeAttributeKey,
        ICollection<CombatImpactAuthoritativeMetric> metrics
    )
    {
        if (!stats.TryGetValue(stat, out var value) || value == 0)
            return;
        metrics.Add(
            new CombatImpactAuthoritativeMetric(
                kind,
                nativeAttributeKey,
                value,
                CombatImpactValueUnit.Amount,
                CombatImpactAuthoritativeBasis.TotalAmount
            )
        );
    }

    private static void AddApplicationMetric(
        IReadOnlyDictionary<CombatCardStat, int> stats,
        CombatCardStat stat,
        CombatImpactKind kind,
        ICollection<CombatImpactAuthoritativeMetric> metrics
    )
    {
        if (!stats.TryGetValue(stat, out var value) || value == 0)
            return;
        metrics.Add(
            new CombatImpactAuthoritativeMetric(
                kind,
                CombatImpactAggregator.NativeKey(kind),
                value,
                CombatImpactValueUnit.Applications,
                CombatImpactAuthoritativeBasis.ApplicationCount
            )
        );
    }

    private readonly record struct ResolvedImpactValue(
        int? Value,
        CombatImpactValueUnit Unit,
        string NativeAttributeKey,
        bool IsCritical,
        CombatImpactValueBasis Basis
    )
    {
        internal CombatImpactEventSurface Surface { get; init; } =
            CombatImpactEventSurface.AppliedEffect;

        internal int CriticalCount { get; init; }

        /// <summary>Number of critical/non-critical outcomes resolved for this execution.</summary>
        internal int CriticalOutcomeCount { get; init; }

        internal int? CriticalValue { get; init; }

        internal int? NonCriticalValue { get; init; }

        internal int? AlternateNonCriticalValue { get; init; }

        internal bool HasCriticalAdjustmentCandidate { get; init; }

        internal bool IsUnattributedTransitionClaimant { get; init; }

        internal int? UnattributedTransitionValue { get; init; }

        internal int? AttributeTransitionNetValue { get; init; }

        internal CombatImpactAttributeTransitionResolution? AttributeTransitionResolution { get; init; }

        internal IReadOnlyList<CombatImpactAttributeTransitionFailureReason> AttributeTransitionFailureReasons { get; init; } =
        [];

        internal int AttributeTransitionUnresolvedClaimantCount { get; init; }

        internal bool AttributeTransitionEventOrderReplayReconciles { get; init; }

        internal bool AttributeTransitionEventOrderReplayIncludesMultiply { get; init; }

        internal static ResolvedImpactValue Empty(CombatImpactKind kind, CombatActionType action) =>
            new(
                null,
                kind
                    is CombatImpactKind.Charge
                        or CombatImpactKind.Haste
                        or CombatImpactKind.Slow
                        or CombatImpactKind.Freeze
                    ? CombatImpactValueUnit.Milliseconds
                    : CombatImpactValueUnit.Amount,
                NativeKeyForAction(action, CombatImpactAggregator.NativeKey(kind)),
                false,
                CombatImpactValueBasis.None
            );
    }

    private readonly record struct AuraAttributeCandidate(
        string SourceId,
        string TargetId,
        CombatCardAttributeUpdate Change,
        string? DirectSourceId,
        string? TriggerSourceId,
        TriggerProvenance Provenance
    );

    private readonly record struct AuraPlayerAttributeCandidate(
        string SourceId,
        string TargetId,
        CombatPlayerAttributeUpdate Change,
        string? DirectSourceId,
        string? TriggerSourceId,
        TriggerProvenance Provenance
    );

    private readonly record struct ProjectedExecution(
        string? SourceId,
        string? TargetId,
        CombatImpactKind? Kind,
        ResolvedImpactValue Resolved,
        string? DirectSourceId,
        string? TriggerSourceId,
        int FrameIndex,
        string? EffectId,
        CombatActionType ActionType,
        string? ExecutionContextId
    )
    {
        internal bool PrerequisiteSkillSource { get; init; }

        internal bool IsCritCapable { get; init; }
    }

    private readonly record struct LifestealCandidate(
        string SourceId,
        int Damage,
        ProjectedExecution Execution
    );

    private readonly record struct PrerequisiteSkillSourceResolution(
        CombatImpactEntity? Implementation,
        CombatImpactEntity? Skill,
        CombatImpactUseAttributionRule? UseRule,
        CombatImpactProjectionDiagnostic? Diagnostic
    );

    private readonly record struct UseAttributionKey(
        string ImplementationId,
        string EffectId,
        string TriggerSourceId,
        string SkillId
    );

    private readonly record struct RuleEffectKey(string ImplementationId, string EffectId);

    private readonly record struct TriggerProvenance(
        CombatImpactActivitySourceResolution SourceResolution,
        CombatImpactTriggerScope Scope
    );

    private readonly record struct IndexedImpactEvent(int Index, CombatImpactEvent Event);

    private readonly record struct NativeActivationKey(
        int FrameIndex,
        string? RawDirectSourceId,
        string? TriggerSourceId
    );

    private readonly record struct DamageCriticalRecoveryKey(
        string SourceId,
        string NativeAttributeKey
    );

    private readonly record struct CriticalTriggerEvidenceKey(string SourceId, int FrameIndex);

    private readonly record struct CriticalTriggerExecutionKey(
        int FrameIndex,
        string DirectSourceId,
        string TriggerSourceId,
        string EffectId,
        string? ExecutionContextId
    );

    private readonly record struct CriticalTriggerOriginExecutionKey(
        CriticalTriggerEvidenceKey Origin,
        CriticalTriggerExecutionKey Execution
    );

    private readonly record struct CriticalTriggerSelfExecutionKey(
        int FrameIndex,
        string DirectSourceId,
        string TriggerSourceId
    );

    private readonly record struct CriticalTriggerExecutionEvidence(
        string TriggerSourceId,
        int FrameIndex,
        CombatEffectPriority Priority,
        string ListenerSourceId,
        string EffectId,
        string? ExecutionContextId
    );

    private sealed class CriticalTriggerAttributionIndex
    {
        private readonly HashSet<CriticalTriggerExecutionKey> _observedExecutionKeys;
        private readonly Dictionary<CriticalTriggerEvidenceKey, int> _candidateCounts = [];
        private readonly Dictionary<
            CriticalTriggerOriginExecutionKey,
            int
        > _candidateExecutionCounts = [];
        private readonly Dictionary<CriticalTriggerSelfExecutionKey, int> _selfExecutionCounts = [];
        private readonly Dictionary<CriticalTriggerExecutionKey, int> _observedExecutionCounts = [];
        private readonly List<int> _listenerEventIndices = [];

        internal CriticalTriggerAttributionIndex(
            IList<CombatImpactEvent> events,
            IReadOnlyList<ProjectedExecution> executions,
            IReadOnlyList<CriticalTriggerExecutionEvidence> observed
        )
        {
            WorkUnitCount = executions.Count;
            _observedExecutionKeys = observed.Select(ExecutionKey).ToHashSet();
            for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                CountWorkUnit();
                var item = events[eventIndex];
                var executionKey = TryExecutionKey(item);
                if (executionKey.HasValue && _observedExecutionKeys.Contains(executionKey.Value))
                    _listenerEventIndices.Add(eventIndex);

                if (!item.TriggerFrameIndex.HasValue || !CanReceiveCriticalTriggerEvidence(item))
                    continue;
                var origin = new CriticalTriggerEvidenceKey(
                    item.SourceId,
                    item.TriggerFrameIndex.Value
                );
                Increment(_candidateCounts, origin);
                if (executionKey.HasValue && _observedExecutionKeys.Contains(executionKey.Value))
                {
                    Increment(
                        _candidateExecutionCounts,
                        new CriticalTriggerOriginExecutionKey(origin, executionKey.Value)
                    );
                }
            }

            foreach (var execution in executions)
            {
                CountWorkUnit();
                if (
                    execution.DirectSourceId is { Length: > 0 } directSourceId
                    && execution.TriggerSourceId is { Length: > 0 } triggerSourceId
                    && string.Equals(directSourceId, triggerSourceId, StringComparison.Ordinal)
                )
                {
                    Increment(
                        _selfExecutionCounts,
                        new CriticalTriggerSelfExecutionKey(
                            execution.FrameIndex,
                            directSourceId,
                            triggerSourceId
                        )
                    );
                }

                var executionKey = TryExecutionKey(execution);
                if (executionKey.HasValue && _observedExecutionKeys.Contains(executionKey.Value))
                    Increment(_observedExecutionCounts, executionKey.Value);
            }
        }

        internal IReadOnlyList<int> ListenerEventIndices => _listenerEventIndices;

        internal long WorkUnitCount { get; private set; }

        internal void CountWorkUnit() => WorkUnitCount++;

        internal CriticalTriggerEvidenceKey? TryResolveOrigin(
            CriticalTriggerExecutionEvidence evidence
        )
        {
            CountWorkUnit();
            var sameFrameOrigin = new CriticalTriggerEvidenceKey(
                evidence.TriggerSourceId,
                evidence.FrameIndex
            );
            var previousFrameOrigin = new CriticalTriggerEvidenceKey(
                evidence.TriggerSourceId,
                evidence.FrameIndex - 1
            );
            var executionKey = ExecutionKey(evidence);
            var sameFrame = HasCandidate(sameFrameOrigin, executionKey);
            var previousFrame = HasCandidate(previousFrameOrigin, executionKey);
            if (evidence.Priority == CombatEffectPriority.Immediate)
            {
                if (sameFrame)
                    return sameFrameOrigin;
                return previousFrame ? previousFrameOrigin : null;
            }

            if (previousFrame && !HasSelfTriggeredExecution(evidence, executionKey))
                return previousFrameOrigin;
            return sameFrame ? sameFrameOrigin : null;
        }

        private bool HasCandidate(
            CriticalTriggerEvidenceKey origin,
            CriticalTriggerExecutionKey listenerExecution
        )
        {
            CountWorkUnit();
            return _candidateCounts.GetValueOrDefault(origin)
                > _candidateExecutionCounts.GetValueOrDefault(
                    new CriticalTriggerOriginExecutionKey(origin, listenerExecution)
                );
        }

        private bool HasSelfTriggeredExecution(
            CriticalTriggerExecutionEvidence evidence,
            CriticalTriggerExecutionKey listenerExecution
        )
        {
            CountWorkUnit();
            var selfCount = _selfExecutionCounts.GetValueOrDefault(
                new CriticalTriggerSelfExecutionKey(
                    evidence.FrameIndex,
                    evidence.TriggerSourceId,
                    evidence.TriggerSourceId
                )
            );
            var listenerCount = string.Equals(
                evidence.ListenerSourceId,
                evidence.TriggerSourceId,
                StringComparison.Ordinal
            )
                ? _observedExecutionCounts.GetValueOrDefault(listenerExecution)
                : 0;
            return selfCount > listenerCount;
        }

        private static void Increment<TKey>(IDictionary<TKey, int> counts, TKey key)
            where TKey : notnull
        {
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }
    }

    private readonly record struct AttributeTransitionResidualKey(
        int FrameIndex,
        string TargetId,
        string NativeAttributeKey,
        CombatImpactValueUnit Unit
    );

    private readonly record struct CriticalRecoveryResult(int CriticalCount, int? CriticalValue);

    private readonly record struct CriticalRecoveryRange(
        int MinimumCriticalCount,
        int MaximumCriticalCount,
        long MinimumCriticalValue,
        long MaximumCriticalValue
    );

    private enum ImpactTransitionDomain
    {
        HealthAdjustment,
        PlayerAttribute,
        CardAttribute,
    }

    private readonly record struct ImpactTransitionClaim(
        ImpactTransitionDomain Domain,
        int Attribute
    );
}
