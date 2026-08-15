#nullable enable
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.GameInterop.CombatSimulation;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactProjector
{
    internal static CombatImpactReport Project(
        CombatSim simulation,
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
            if (stats.TryGetValue(ECardStats.UseCount, out var uses))
                useCounts[sourceId] = uses;

            var metrics = new List<CombatImpactAuthoritativeMetric>();
            AddAmountMetric(
                stats,
                ECardStats.DamageDone,
                CombatImpactKind.DirectDamage,
                CombatImpactAggregator.NativeKey(CombatImpactKind.DirectDamage),
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.BurnAdded,
                CombatImpactKind.Burn,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Burn),
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.PoisonAdded,
                CombatImpactKind.Poison,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Poison),
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.HealAdded,
                CombatImpactKind.Healing,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Healing),
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.ShieldAdded,
                CombatImpactKind.Shield,
                CombatImpactAggregator.NativeKey(CombatImpactKind.Shield),
                metrics
            );
            AddApplicationMetric(
                stats,
                ECardStats.HastedCardsCount,
                CombatImpactKind.Haste,
                metrics
            );
            AddApplicationMetric(
                stats,
                ECardStats.SlowedCardsCount,
                CombatImpactKind.Slow,
                metrics
            );
            AddApplicationMetric(
                stats,
                ECardStats.FrozenCardsCount,
                CombatImpactKind.Freeze,
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.RegenAdded,
                CombatImpactKind.AttributeChange,
                "RegenApplyAmount",
                metrics
            );
            AddAmountMetric(
                stats,
                ECardStats.RageAdded,
                CombatImpactKind.AttributeChange,
                "RageApplyAmount",
                metrics
            );
            if (OptionalCombatTempoTypes.TryGetAddedStat(out var tempoAdded))
            {
                AddAmountMetric(
                    stats,
                    tempoAdded,
                    CombatImpactKind.AttributeChange,
                    "TempoApplyAmount",
                    metrics
                );
            }
            if (OptionalCombatTempoTypes.TryGetSpentStat(out var tempoSpent))
            {
                AddAmountMetric(
                    stats,
                    tempoSpent,
                    CombatImpactKind.AttributeChange,
                    "TempoRemoveAmount",
                    metrics
                );
            }
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
                entities,
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
                || implementation.Section != EInventorySection.Hand
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
                        && entity.Section == EInventorySection.Hand
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
        var leftStart = (int)left.SocketId!.Value;
        var leftEnd = leftStart + Math.Max(1, left.DisplaySpan);
        var rightStart = (int)right.SocketId!.Value;
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
            PeriodicResiduals = periodic.Residuals,
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
                                            .Targets.Select(target => target.Entity.CombatantId)
                                            .OfType<ECombatantId>()
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
        IReadOnlyCollection<ECombatantId> targetCombatants,
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
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var projections = new List<ProjectedExecution>();
        var cardAttributes = CreateCardAttributeTimeline(entities);
        for (var frameIndex = 0; frameIndex < simulation.Frames.Count; frameIndex++)
        {
            var frame = simulation.Frames[frameIndex];
            ReconcileCardAttributeTimeline(frame, cardAttributes, usePreviousValue: true);
            var executed = frame.Events.OfType<CombatSimEventEffectExecuted>().ToArray();
            foreach (var item in executed)
            {
                var attributedSourceId = ResolveActivitySource(
                    item.Source?.Value,
                    item.TriggerSource?.Value,
                    entities
                );
                var isCritCapable = IsCritCapable(
                    item.Source?.Value,
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
                        item.Source?.Value,
                        item.TriggerSource?.Value,
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
                && item.Surface == CombatImpactEventSurface.CardAttribute
                && item.IsUnattributedTransitionClaimant
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
                return values.Length == 1
                    ? new CombatImpactAttributeTransitionResidual(
                        group.Key.FrameIndex,
                        group.Key.TargetId,
                        group.Key.NativeAttributeKey,
                        values[0],
                        group.Key.Unit,
                        group.Count(),
                        CombatImpactAttributeTransitionResidualReason.ConcurrentAttributionUnavailable
                    )
                    : null;
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
                var residualValue =
                    resolution == CombatImpactAttributeTransitionResolution.ConcurrentResidual
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
        CombatSim simulation,
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
                ECombatantId.Player,
                frame.PlayerUpdates,
                frameIndex,
                entities,
                cardAttributes,
                frameExecutions,
                events
            );
            AddLifestealEvents(
                ECombatantId.Opponent,
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
        ECombatantId combatant,
        CombatSimPlayerUpdate? update,
        int frameIndex,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        IReadOnlyList<ProjectedExecution> frameExecutions,
        ICollection<CombatImpactEvent> events
    )
    {
        var realizedHealing = ResolveUnrecordedHealthGain(update);
        if (realizedHealing <= 0)
            return;

        var opposingCombatant =
            combatant == ECombatantId.Player ? ECombatantId.Opponent : ECombatantId.Player;
        var candidates = frameExecutions
            .Where(execution =>
                execution.ActionType == EActionCommandType.PlayerDamage
                && execution.SourceId != null
                && execution.TargetId == PlayerId(opposingCombatant)
                && execution.Resolved.Value is > 0
                && entities.TryGetValue(execution.SourceId, out var source)
                && source.TypeLabel == "Item"
                && source.CombatantId == combatant
                && cardAttributes.TryGetValue(execution.SourceId, out var attributes)
                && attributes.GetValueOrDefault(ECardAttributeType.Lifesteal) > 0
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

    private static int ResolveUnrecordedHealthGain(CombatSimPlayerUpdate? update)
    {
        if (
            update == null
            || !update.Attributes.TryGetValue(EPlayerAttributeType.Health, out var health)
        )
            return 0;

        var recordedHealthMovement = update
            .HealthAdjustments.Where(adjustment =>
                adjustment.AttributeChanged == EPlayerHealthChangeType.Health
            )
            .Sum(adjustment => (long)adjustment.Amount);
        var residual = (long)health.Delta - recordedHealthMovement;
        return residual > 0 ? SaturatingInt(residual) : 0;
    }

    private static void AddCardActionCostEvents(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        ICollection<CombatImpactEvent> events
    )
    {
        foreach (var frame in simulation.Frames)
        {
            foreach (var candidate in frame.Events)
            {
                if (
                    !CardActionCostSpentEventReader.TryRead(candidate, out var costSpent)
                    || costSpent.PlayerAttributeSpent != EPlayerAttributeType.Tempo
                )
                    continue;

                var sourceId = costSpent.ExecutingCard.Value;
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
        Dictionary<ECardAttributeType, int>
    > CreateCardAttributeTimeline(IReadOnlyDictionary<string, CombatImpactEntity> entities)
    {
        var timeline = new Dictionary<string, Dictionary<ECardAttributeType, int>>(
            StringComparer.Ordinal
        );
        foreach (var entity in entities.Values)
        {
            if (entity.Attributes == null)
                continue;
            timeline[entity.Id] = new Dictionary<ECardAttributeType, int>(entity.Attributes);
        }

        return timeline;
    }

    private static void ReconcileCardAttributeTimeline(
        CombatSimFrame frame,
        IDictionary<string, Dictionary<ECardAttributeType, int>> timeline,
        bool usePreviousValue
    )
    {
        foreach (var (instanceId, cardUpdate) in frame.CardUpdates)
        {
            var cardId = instanceId.Value;
            if (!timeline.TryGetValue(cardId, out var attributes))
            {
                attributes = new Dictionary<ECardAttributeType, int>();
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
        EActionCommandType action,
        string sourceId,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        out ResolvedImpactValue resolved
    )
    {
        (ECardAttributeType Attribute, CombatImpactKind Kind)? configuration = action switch
        {
            EActionCommandType.CardCharge => (
                ECardAttributeType.ChargeAmount,
                CombatImpactKind.Charge
            ),
            EActionCommandType.CardHaste => (
                ECardAttributeType.HasteAmount,
                CombatImpactKind.Haste
            ),
            EActionCommandType.CardSlow => (ECardAttributeType.SlowAmount, CombatImpactKind.Slow),
            EActionCommandType.CardFreeze => (
                ECardAttributeType.FreezeAmount,
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
        EActionCommandType action,
        string sourceId,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        CombatSimFrame frame,
        out int value,
        out int? alternateValue
    )
    {
        var attribute = action switch
        {
            EActionCommandType.PlayerDamage => ECardAttributeType.DamageAmount,
            EActionCommandType.PlayerBurnApply => ECardAttributeType.BurnApplyAmount,
            EActionCommandType.PlayerPoisonApply => ECardAttributeType.PoisonApplyAmount,
            EActionCommandType.PlayerRegenApply => ECardAttributeType.RegenApplyAmount,
            _ => (ECardAttributeType?)null,
        };
        if (
            !attribute.HasValue
            && OptionalCombatTempoTypes.TryGetCardAttribute(action, out var tempoAttribute)
        )
            attribute = tempoAttribute;
        if (
            attribute.HasValue
            && cardAttributes.TryGetValue(sourceId, out var attributes)
            && attributes.TryGetValue(attribute.Value, out value)
            && value > 0
        )
        {
            alternateValue = null;
            var sourceInstance = InstanceId.TryParse(sourceId);
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

    private static bool HasCriticalConfiguredAmount(EActionCommandType action) =>
        action
            is EActionCommandType.PlayerDamage
                or EActionCommandType.PlayerBurnApply
                or EActionCommandType.PlayerPoisonApply
                or EActionCommandType.PlayerRegenApply;

    private static ResolvedImpactValue ResolveConfiguredStatusCriticalOutcome(
        EActionCommandType action,
        ResolvedImpactValue resolved,
        bool isCritCapable
    )
    {
        if (
            !isCritCapable
            || action
                is not (
                    EActionCommandType.PlayerBurnApply
                    or EActionCommandType.PlayerPoisonApply
                    or EActionCommandType.PlayerRegenApply
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
        var observed = executions
            .Where(execution =>
                execution.DirectSourceId is { Length: > 0 } sourceId
                && execution.TriggerSourceId is { Length: > 0 }
                && execution.EffectId is { Length: > 0 } effectId
                && entities.TryGetValue(sourceId, out var source)
                && source.CriticalTriggerAbilitiesByEffectId?.ContainsKey(effectId) == true
            )
            .Select(execution => new CriticalTriggerExecutionEvidence(
                execution.TriggerSourceId!,
                execution.FrameIndex,
                entities[execution.DirectSourceId!].CriticalTriggerAbilitiesByEffectId![
                    execution.EffectId!
                ],
                execution.DirectSourceId!,
                execution.EffectId!,
                execution.ExecutionContextId
            ))
            .Distinct()
            .ToArray();
        var resolutions = observed
            .Select(evidence => TryResolveCriticalTriggerOrigin(events, executions, evidence))
            .ToArray();
        var evidence = resolutions
            .Where(origin => origin.HasValue)
            .Select(origin => origin!.Value)
            .ToHashSet();
        if (evidence.Count == 0)
            return new CombatImpactCriticalTriggerEvidenceAudit(0, 0);

        var candidates = events
            .Select((item, index) => new IndexedImpactEvent(index, item))
            .Where(item =>
                item.Event.TriggerFrameIndex.HasValue
                && evidence.Contains(
                    new CriticalTriggerEvidenceKey(
                        item.Event.SourceId,
                        item.Event.TriggerFrameIndex.Value
                    )
                )
                && CanReceiveCriticalTriggerEvidence(item.Event)
            )
            .GroupBy(item => new CriticalTriggerEvidenceKey(
                item.Event.SourceId,
                item.Event.TriggerFrameIndex!.Value
            ));

        var candidatesByOrigin = candidates.ToDictionary(
            group => group.Key,
            group => group.ToArray()
        );
        var attributed = 0;
        foreach (var origin in evidence)
        {
            if (!candidatesByOrigin.TryGetValue(origin, out var items))
                continue;
            if (items.Any(item => item.Event.CriticalCount > 0 || item.Event.IsCritical))
            {
                attributed++;
                continue;
            }

            var originating = SelectCriticalTriggerOrigin(items);
            events[originating.Index] = events[originating.Index] with
            {
                CriticalCount = 1,
                CriticalOutcomeCount = 1,
                CriticalValue = null,
            };
            attributed++;
        }
        return new CombatImpactCriticalTriggerEvidenceAudit(evidence.Count, attributed);
    }

    private static CriticalTriggerEvidenceKey? TryResolveCriticalTriggerOrigin(
        IEnumerable<CombatImpactEvent> events,
        IReadOnlyList<ProjectedExecution> executions,
        CriticalTriggerExecutionEvidence evidence
    )
    {
        var candidateFrames = events
            .Where(item =>
                string.Equals(item.SourceId, evidence.TriggerSourceId, StringComparison.Ordinal)
                && item.TriggerFrameIndex is { } frameIndex
                && frameIndex <= evidence.FrameIndex
                && frameIndex >= evidence.FrameIndex - 1
                && CanReceiveCriticalTriggerEvidence(item)
            )
            .Select(item => item.TriggerFrameIndex!.Value)
            .Distinct()
            .OrderByDescending(frameIndex => frameIndex)
            .ToArray();
        if (candidateFrames.Length == 0)
            return null;

        var sameFrame = candidateFrames.Contains(evidence.FrameIndex);
        var previousFrame = candidateFrames.Contains(evidence.FrameIndex - 1);
        var originFrame = evidence.Priority switch
        {
            EEffectPriority.Immediate when sameFrame => evidence.FrameIndex,
            EEffectPriority.Immediate when previousFrame => evidence.FrameIndex - 1,
            _ when previousFrame
                    && !HasSelfTriggeredExecution(
                        executions,
                        evidence.TriggerSourceId,
                        evidence.FrameIndex
                    ) => evidence.FrameIndex - 1,
            _ when sameFrame => evidence.FrameIndex,
            _ => -1,
        };

        return originFrame >= 0
            ? new CriticalTriggerEvidenceKey(evidence.TriggerSourceId, originFrame)
            : null;
    }

    private static bool HasSelfTriggeredExecution(
        IEnumerable<ProjectedExecution> executions,
        string sourceId,
        int frameIndex
    ) =>
        executions.Any(execution =>
            execution.FrameIndex == frameIndex
            && string.Equals(execution.DirectSourceId, sourceId, StringComparison.Ordinal)
            && string.Equals(execution.TriggerSourceId, sourceId, StringComparison.Ordinal)
        );

    private static bool CanReceiveCriticalTriggerEvidence(CombatImpactEvent item) =>
        item.Surface == CombatImpactEventSurface.AppliedEffect
        && item.IsCritCapable
        && item.OccurrenceBasis == CombatImpactOccurrenceBasis.ExplicitExecution;

    private static IndexedImpactEvent SelectCriticalTriggerOrigin(
        IReadOnlyList<IndexedImpactEvent> candidates
    )
    {
        foreach (
            var preferredKind in new[]
            {
                CombatImpactKind.DirectDamage,
                CombatImpactKind.Burn,
                CombatImpactKind.Poison,
                CombatImpactKind.Healing,
                CombatImpactKind.Shield,
                CombatImpactKind.AttributeChange,
            }
        )
        {
            var matches = candidates.Where(item => item.Event.Kind == preferredKind).ToArray();
            if (matches.Length == 1)
                return matches[0];
        }

        return candidates[0];
    }

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
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind
    )
    {
        if (item.Target is not EffectTargetPlayer playerTarget)
            return false;

        var update =
            playerTarget.Target == ECombatantId.Player
                ? frame.PlayerUpdates
                : frame.OpponentUpdates;
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
            || implementation.Section != EInventorySection.Hand
            || !implementation.SocketId.HasValue
            || string.IsNullOrWhiteSpace(execution.TriggerSourceId)
            || !entities.TryGetValue(execution.TriggerSourceId!, out var triggerItem)
            || triggerItem.TypeLabel != "Item"
            || triggerItem.CombatantId != combatantId
            || triggerItem.Section != EInventorySection.Hand
            || !triggerItem.SocketId.HasValue
            || !rule.ItemCondition.Matches(triggerItem)
            || !SpansOverlap(triggerItem, implementation)
            || execution.TargetId != PlayerId(combatantId)
            || execution.Kind != CombatImpactKind.AttributeChange
            || (
                execution.ActionType != EActionCommandType.PlayerModifyAttribute
                && !OptionalCombatTempoTypes.IsApplyAction(execution.ActionType)
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
        CombatSimEventEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out ECardAttributeType attributeType
    )
    {
        if (
            TryResolveEffectAttributeType(
                effect.Source?.Value,
                effect.TriggerSource?.Value,
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
        CombatSimEventEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out string sourceId,
        out TActionCardModifyAttribute modifier
    )
    {
        if (
            TryResolveAbilityAttributeModifier(
                effect.Source?.Value,
                effect.EffectId,
                entities,
                out sourceId,
                out modifier
            )
        )
            return true;
        if (
            !string.Equals(
                effect.Source?.Value,
                effect.TriggerSource?.Value,
                StringComparison.Ordinal
            )
            && TryResolveAbilityAttributeModifier(
                effect.TriggerSource?.Value,
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
        out TActionCardModifyAttribute modifier
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
        CombatSimEventEffectAuraExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out ECardAttributeType attributeType
    ) =>
        TryResolveEffectAttributeType(
            effect.Source?.Value,
            effect.TriggerSource?.Value,
            effect.EffectId,
            entities,
            static entity => entity.AuraAttributeTypesByEffectId,
            out attributeType
        );

    private static bool TryResolveEffectAttributeType(
        string? directSourceId,
        string? triggerSourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        Func<CombatImpactEntity, IReadOnlyDictionary<string, ECardAttributeType>?> selectMappings,
        out ECardAttributeType attributeType
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
        Func<CombatImpactEntity, IReadOnlyDictionary<string, ECardAttributeType>?> selectMappings,
        out ECardAttributeType attributeType
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
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        ICollection<CombatImpactEvent> events,
        ICollection<CombatImpactProjectionDiagnostic> diagnostics
    )
    {
        foreach (var frame in simulation.Frames)
        {
            var candidates = new List<AuraAttributeCandidate>();
            var playerCandidates = new List<AuraPlayerAttributeCandidate>();
            foreach (var aura in frame.Events.OfType<CombatSimEventEffectAuraExecuted>())
            {
                var directSourceId = aura.Source?.Value;
                var triggerSourceId = aura.TriggerSource?.Value;
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
                    if (target is EffectTargetPlayer playerTarget)
                    {
                        var playerId = PlayerId(playerTarget.Target);
                        var playerUpdate =
                            playerTarget.Target == ECombatantId.Player
                                ? frame.PlayerUpdates
                                : frame.OpponentUpdates;
                        var playerChanges = playerUpdate
                            ?.Attributes.Values.Where(change =>
                                change.Delta != 0
                                && IsDisplayableAuraPlayerAttribute(change.AttributeType)
                            )
                            .ToArray();
                        if (playerChanges?.Length == 1 && entities.ContainsKey(playerId))
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
                        target is not EffectTargetCard cardTarget
                        || !entities.ContainsKey(cardTarget.Target.Value)
                        || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
                    )
                        continue;
                    if (
                        isReferenceValuedAura
                        && string.Equals(
                            sourceId,
                            cardTarget.Target.Value,
                            StringComparison.Ordinal
                        )
                    )
                        continue;

                    CombatSimCardAttributeUpdate cardChange;
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
                            cardTarget.Target.Value,
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
        CombatSimEventEffectAuraExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        HasReferenceValuedAuraEffect(effect.Source?.Value, effect.EffectId, entities)
        || HasReferenceValuedAuraEffect(effect.TriggerSource?.Value, effect.EffectId, entities);

    private static bool HasReferenceValuedAuraEffect(
        string? sourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        !string.IsNullOrWhiteSpace(sourceId)
        && !string.IsNullOrWhiteSpace(effectId)
        && entities.TryGetValue(sourceId!, out var source)
        && source.ReferenceValuedAuraEffectIds?.Contains(effectId!) == true;

    private static bool HasExplicitModifierClaim(CombatSimFrame frame, InstanceId target) =>
        frame
            .Events.OfType<CombatSimEventEffectExecuted>()
            .Any(item =>
                item.ActionType == EActionCommandType.CardModifyAttribute
                && item.Target is EffectTargetCard cardTarget
                && cardTarget.Target == target
            );

    private static bool HasExplicitModifierClaim(
        CombatSimFrame frame,
        InstanceId target,
        ECardAttributeType attributeType,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    ) =>
        frame
            .Events.OfType<CombatSimEventEffectExecuted>()
            .Any(item =>
                item.ActionType == EActionCommandType.CardModifyAttribute
                && item.Target is EffectTargetCard cardTarget
                && cardTarget.Target == target
                && (
                    !TryResolveAbilityAttributeType(item, entities, out var claimedAttribute)
                    || claimedAttribute == attributeType
                )
            );

    private static bool IsAttributableCardAttribute(ECardAttributeType attribute) =>
        IsDisplayableAuraAttribute(attribute)
        || attribute.ToString().StartsWith("Custom_", StringComparison.Ordinal);

    private static bool IsDisplayableAuraAttribute(ECardAttributeType attribute) =>
        OptionalCombatTempoTypes.IsAmountAttribute(attribute)
        || attribute
            is ECardAttributeType.AmmoMax
                or ECardAttributeType.ReloadAmount
                or ECardAttributeType.ReloadTargets
                or ECardAttributeType.CooldownMax
                or ECardAttributeType.ChargeAmount
                or ECardAttributeType.ChargeTargets
                or ECardAttributeType.HasteAmount
                or ECardAttributeType.HasteTargets
                or ECardAttributeType.SlowAmount
                or ECardAttributeType.SlowTargets
                or ECardAttributeType.FreezeAmount
                or ECardAttributeType.FreezeTargets
                or ECardAttributeType.BurnApplyAmount
                or ECardAttributeType.BurnRemoveAmount
                or ECardAttributeType.PoisonApplyAmount
                or ECardAttributeType.PoisonRemoveAmount
                or ECardAttributeType.Multicast
                or ECardAttributeType.Lifesteal
                or ECardAttributeType.CritChance
                or ECardAttributeType.DamageAmount
                or ECardAttributeType.DamageCrit
                or ECardAttributeType.HealAmount
                or ECardAttributeType.HealCrit
                or ECardAttributeType.JoyApplyAmount
                or ECardAttributeType.JoyRemoveAmount
                or ECardAttributeType.JoyCrit
                or ECardAttributeType.ShieldApplyAmount
                or ECardAttributeType.ShieldRemoveAmount
                or ECardAttributeType.ShieldCrit
                or ECardAttributeType.ForceUseTargets
                or ECardAttributeType.EnchantTargets
                or ECardAttributeType.UpgradeTargets
                or ECardAttributeType.DisableTargets
                or ECardAttributeType.RepairTargets
                or ECardAttributeType.BurnCrit
                or ECardAttributeType.PoisonCrit
                or ECardAttributeType.DestroyTargets
                or ECardAttributeType.RegenApplyAmount
                or ECardAttributeType.RegenRemoveAmount
                or ECardAttributeType.RegenCrit
                or ECardAttributeType.TransformTargets
                or ECardAttributeType.FlatCooldownReduction
                or ECardAttributeType.PercentCooldownReduction
                or ECardAttributeType.EnchantRemoveTargets
                or ECardAttributeType.FlyingTargets
                or ECardAttributeType.PercentChargeReduction
                or ECardAttributeType.PercentHasteReduction
                or ECardAttributeType.PercentSlowReduction
                or ECardAttributeType.PercentFreezeReduction
                or ECardAttributeType.DestroyImmunity
                or ECardAttributeType.RageApplyAmount
                or ECardAttributeType.RageRemoveAmount
                or ECardAttributeType.TempoCost
                or ECardAttributeType.FlatTempoCostReduction
                or ECardAttributeType.PercentTempoCostReduction
                or ECardAttributeType.BuyPrice
                or ECardAttributeType.SellPrice;

    private static bool IsDisplayableAuraPlayerAttribute(EPlayerAttributeType attribute) =>
        attribute
            is EPlayerAttributeType.CritChance
                or EPlayerAttributeType.DamageCrit
                or EPlayerAttributeType.JoyCrit
                or EPlayerAttributeType.HealthMax
                or EPlayerAttributeType.HealthRegen
                or EPlayerAttributeType.HealAmount
                or EPlayerAttributeType.HealCrit
                or EPlayerAttributeType.ShieldCrit
                or EPlayerAttributeType.FlatDamageReduction
                or EPlayerAttributeType.PercentDamageReduction
                or EPlayerAttributeType.Rage
                or EPlayerAttributeType.RageMax
                or EPlayerAttributeType.EnragedDurationMax
                or EPlayerAttributeType.Tempo
                or EPlayerAttributeType.TempoGainCooldownMax
                or EPlayerAttributeType.FlatTempoGainCooldownReduction
                or EPlayerAttributeType.PercentTempoGainCooldownReduction
                or EPlayerAttributeType.Experience
                or EPlayerAttributeType.Gold
                or EPlayerAttributeType.Income
                or EPlayerAttributeType.Prestige
                or EPlayerAttributeType.Level
                or EPlayerAttributeType.RerollCostModifier;

    internal static string PlayerId(ECombatantId combatant) => $"player:{combatant}";

    private static string? ResolveTargetId(IEffectTarget? target) =>
        target switch
        {
            EffectTargetCard card => card.Target.Value,
            EffectTargetPlayer player => PlayerId(player.Target),
            _ => null,
        };

    private static bool IsTransitionUnique(
        CombatSimFrame frame,
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        CombatSimEventEffectExecuted candidate,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (!TryResolveTransitionClaim(candidate.ActionType, out var transition))
            return false;
        var targetId = ResolveTargetId(candidate.Target);
        var claimCount = executed.Count(item =>
            string.Equals(ResolveTargetId(item.Target), targetId, StringComparison.Ordinal)
            && ClaimsTransition(item.ActionType, transition)
        );
        if (
            transition.Domain == ImpactTransitionDomain.PlayerAttribute
            && transition.Attribute == (int)EPlayerAttributeType.Tempo
        )
        {
            claimCount += frame.Events.Count(candidate =>
                CardActionCostSpentEventReader.TryRead(candidate, out var costSpent)
                && costSpent.PlayerAttributeSpent == EPlayerAttributeType.Tempo
                && string.Equals(
                    ResolveCardActionCostTargetId(costSpent, entities),
                    targetId,
                    StringComparison.Ordinal
                )
            );
        }

        return claimCount == 1;
    }

    private static string? ResolveCardActionCostTargetId(
        CardActionCostSpentEvent costSpent,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (
            !entities.TryGetValue(costSpent.ExecutingCard.Value, out var source)
            || !source.CombatantId.HasValue
        )
            return null;

        var targetId = PlayerId(source.CombatantId.Value);
        return entities.ContainsKey(targetId) ? targetId : null;
    }

    private static bool ClaimsTransition(
        EActionCommandType action,
        ImpactTransitionClaim transition
    ) => TryResolveTransitionClaim(action, out var candidate) && candidate == transition;

    private static bool TryResolveTransitionClaim(
        EActionCommandType action,
        out ImpactTransitionClaim transition
    )
    {
        if (OptionalCombatTempoTypes.IsAction(action))
        {
            transition = new ImpactTransitionClaim(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.Tempo
            );
            return true;
        }

        transition = action switch
        {
            EActionCommandType.PlayerDamage => new(
                ImpactTransitionDomain.HealthAdjustment,
                (int)CombatImpactKind.DirectDamage
            ),
            EActionCommandType.PlayerHeal => new(
                ImpactTransitionDomain.HealthAdjustment,
                (int)CombatImpactKind.Healing
            ),
            EActionCommandType.PlayerShieldApply or EActionCommandType.PlayerShieldRemove => new(
                ImpactTransitionDomain.HealthAdjustment,
                (int)CombatImpactKind.Shield
            ),
            EActionCommandType.PlayerBurnApply or EActionCommandType.PlayerBurnRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.Burn
            ),
            EActionCommandType.PlayerPoisonApply or EActionCommandType.PlayerPoisonRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.Poison
            ),
            EActionCommandType.PlayerRegenApply or EActionCommandType.PlayerRegenRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.HealthRegen
            ),
            EActionCommandType.PlayerRageApply or EActionCommandType.PlayerRageRemove => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.Rage
            ),
            EActionCommandType.PlayerMaxHealthIncrease
            or EActionCommandType.PlayerMaxHealthDecrease => new(
                ImpactTransitionDomain.PlayerAttribute,
                (int)EPlayerAttributeType.HealthMax
            ),
            EActionCommandType.CardHaste => new(
                ImpactTransitionDomain.CardAttribute,
                (int)ECardAttributeType.Haste
            ),
            EActionCommandType.CardSlow => new(
                ImpactTransitionDomain.CardAttribute,
                (int)ECardAttributeType.Slow
            ),
            EActionCommandType.CardFreeze => new(
                ImpactTransitionDomain.CardAttribute,
                (int)ECardAttributeType.Freeze
            ),
            EActionCommandType.CardReload => new(
                ImpactTransitionDomain.CardAttribute,
                (int)ECardAttributeType.Ammo
            ),
            _ => default,
        };
        return action
            is EActionCommandType.PlayerDamage
                or EActionCommandType.PlayerHeal
                or EActionCommandType.PlayerShieldApply
                or EActionCommandType.PlayerShieldRemove
                or EActionCommandType.PlayerBurnApply
                or EActionCommandType.PlayerBurnRemove
                or EActionCommandType.PlayerPoisonApply
                or EActionCommandType.PlayerPoisonRemove
                or EActionCommandType.PlayerRegenApply
                or EActionCommandType.PlayerRegenRemove
                or EActionCommandType.PlayerRageApply
                or EActionCommandType.PlayerRageRemove
                or EActionCommandType.PlayerMaxHealthIncrease
                or EActionCommandType.PlayerMaxHealthDecrease
                or EActionCommandType.CardHaste
                or EActionCommandType.CardSlow
                or EActionCommandType.CardFreeze
                or EActionCommandType.CardReload;
    }

    internal static bool HasExplicitDisplayClassification(EActionCommandType action) =>
        TryResolveKind(action, out _) || IsExplicitlyIgnoredAction(action);

    internal static bool TryResolveKind(EActionCommandType action, out CombatImpactKind kind)
    {
        if (OptionalCombatTempoTypes.IsAction(action))
        {
            kind = CombatImpactKind.AttributeChange;
            return true;
        }

        kind = action switch
        {
            EActionCommandType.PlayerDamage => CombatImpactKind.DirectDamage,
            EActionCommandType.PlayerBurnApply => CombatImpactKind.Burn,
            EActionCommandType.PlayerPoisonApply => CombatImpactKind.Poison,
            EActionCommandType.PlayerHeal => CombatImpactKind.Healing,
            EActionCommandType.PlayerShieldApply => CombatImpactKind.Shield,
            EActionCommandType.CardCharge => CombatImpactKind.Charge,
            EActionCommandType.CardHaste => CombatImpactKind.Haste,
            EActionCommandType.CardSlow => CombatImpactKind.Slow,
            EActionCommandType.CardFreeze => CombatImpactKind.Freeze,
            EActionCommandType.FlyingStart or EActionCommandType.FlyingStop =>
                CombatImpactKind.Flying,
            EActionCommandType.CardReload => CombatImpactKind.AttributeChange,
            EActionCommandType.CardForceUse
            or EActionCommandType.CardEnchant
            or EActionCommandType.CardEnchantRemove
            or EActionCommandType.CardTransform
            or EActionCommandType.CardTransformDestroyed
            or EActionCommandType.CardUpgrade
            or EActionCommandType.CardRepair => CombatImpactKind.AttributeChange,
            EActionCommandType.CardModifyAttribute => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerModifyAttribute => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerMaxHealthIncrease => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerMaxHealthDecrease => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerRegenApply => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerRageApply => CombatImpactKind.AttributeChange,
            EActionCommandType.PlayerBurnRemove
            or EActionCommandType.PlayerPoisonRemove
            or EActionCommandType.PlayerRegenRemove
            or EActionCommandType.PlayerShieldRemove
            or EActionCommandType.PlayerRageRemove => CombatImpactKind.AttributeChange,
            EActionCommandType.CardDisable or EActionCommandType.CardDestroy =>
                CombatImpactKind.Destroy,
            _ => default,
        };
        return action
            is EActionCommandType.PlayerDamage
                or EActionCommandType.PlayerBurnApply
                or EActionCommandType.PlayerPoisonApply
                or EActionCommandType.PlayerHeal
                or EActionCommandType.PlayerShieldApply
                or EActionCommandType.CardCharge
                or EActionCommandType.CardHaste
                or EActionCommandType.CardSlow
                or EActionCommandType.CardFreeze
                or EActionCommandType.FlyingStart
                or EActionCommandType.FlyingStop
                or EActionCommandType.CardReload
                or EActionCommandType.CardForceUse
                or EActionCommandType.CardEnchant
                or EActionCommandType.CardEnchantRemove
                or EActionCommandType.CardTransform
                or EActionCommandType.CardTransformDestroyed
                or EActionCommandType.CardUpgrade
                or EActionCommandType.CardRepair
                or EActionCommandType.CardModifyAttribute
                or EActionCommandType.PlayerModifyAttribute
                or EActionCommandType.PlayerMaxHealthIncrease
                or EActionCommandType.PlayerMaxHealthDecrease
                or EActionCommandType.PlayerRegenApply
                or EActionCommandType.PlayerRageApply
                or EActionCommandType.PlayerBurnRemove
                or EActionCommandType.PlayerPoisonRemove
                or EActionCommandType.PlayerRegenRemove
                or EActionCommandType.PlayerShieldRemove
                or EActionCommandType.PlayerRageRemove
                or EActionCommandType.CardDisable
                or EActionCommandType.CardDestroy;
    }

    private static bool IsExplicitlyIgnoredAction(EActionCommandType action) =>
        action
            is EActionCommandType.None
                or EActionCommandType.CardAddTags
                or EActionCommandType.CardRemoveTags
                or EActionCommandType.CardBeginSandstorm
                or EActionCommandType.GameDealCards
                or EActionCommandType.GameModifyTime
                or EActionCommandType.GameScheduleEncounter
                or EActionCommandType.GameSpawnCards
                or EActionCommandType.GameStartCombat
                or EActionCommandType.GameUnscheduleEncounter
                or EActionCommandType.PlayerGoldSteal
                or EActionCommandType.PlayerJoyApply
                or EActionCommandType.PlayerJoyRemove
                or EActionCommandType.ExitReplacementSet
                or EActionCommandType.CardAddTagsRandom
                or EActionCommandType.PlayerPortraitNext
                or EActionCommandType.PlayerPortraitReset
                or EActionCommandType.GameReroll;

    private static ResolvedImpactValue ResolveValue(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        bool transitionIsUnique,
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes
    )
    {
        if (
            kind == CombatImpactKind.AttributeChange
            && item.ActionType
                is EActionCommandType.CardModifyAttribute
                    or EActionCommandType.PlayerModifyAttribute
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

        if (item.Target is EffectTargetPlayer playerTarget)
        {
            var update =
                playerTarget.Target == ECombatantId.Player
                    ? frame.PlayerUpdates
                    : frame.OpponentUpdates;
            if (update == null)
                return ResolvedImpactValue.Empty(kind, item.ActionType);

            if (item.ActionType == EActionCommandType.PlayerShieldRemove)
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

            EPlayerAttributeType? expectedAttribute = OptionalCombatTempoTypes.IsAction(
                item.ActionType
            )
                ? EPlayerAttributeType.Tempo
                : item.ActionType switch
                {
                    EActionCommandType.PlayerBurnApply or EActionCommandType.PlayerBurnRemove =>
                        EPlayerAttributeType.Burn,
                    EActionCommandType.PlayerPoisonApply or EActionCommandType.PlayerPoisonRemove =>
                        EPlayerAttributeType.Poison,
                    EActionCommandType.PlayerRegenApply or EActionCommandType.PlayerRegenRemove =>
                        EPlayerAttributeType.HealthRegen,
                    EActionCommandType.PlayerRageApply or EActionCommandType.PlayerRageRemove =>
                        EPlayerAttributeType.Rage,
                    EActionCommandType.PlayerMaxHealthIncrease => EPlayerAttributeType.HealthMax,
                    EActionCommandType.PlayerMaxHealthDecrease => EPlayerAttributeType.HealthMax,
                    _ => (EPlayerAttributeType?)null,
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
            item.Target is not EffectTargetCard cardTarget
            || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
        )
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        if (kind == CombatImpactKind.Destroy)
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        if (item.ActionType == EActionCommandType.CardReload)
        {
            if (
                cardUpdate.Attributes.TryGetValue(ECardAttributeType.Ammo, out var ammo)
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
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        out ResolvedImpactValue resolved
    )
    {
        var nativeKey = item.ActionType switch
        {
            EActionCommandType.CardForceUse => "ForceUseTargets",
            EActionCommandType.CardEnchant => ResolveEnchantActionKey(frame, item),
            EActionCommandType.CardEnchantRemove => "EnchantRemoveTargets",
            EActionCommandType.CardTransform or EActionCommandType.CardTransformDestroyed =>
                "TransformTargets",
            EActionCommandType.CardUpgrade => "UpgradeTargets",
            EActionCommandType.CardRepair => "RepairTargets",
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

    private static string ResolveEnchantActionKey(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item
    )
    {
        if (item.Target is not EffectTargetCard cardTarget)
            return "EnchantTargets";

        var targetId = cardTarget.Target.Value;
        var matchingExecutions = frame
            .Events.OfType<CombatSimEventEffectExecuted>()
            .Where(candidate =>
                candidate.ActionType == EActionCommandType.CardEnchant
                && string.Equals(
                    ResolveTargetId(candidate.Target),
                    targetId,
                    StringComparison.Ordinal
                )
            )
            .Take(2)
            .Count();
        var enchantments = frame
            .Events.OfType<CombatSimEventCardEnchanted>()
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
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes
    )
    {
        if (item.ActionType == EActionCommandType.PlayerModifyAttribute)
        {
            if (item.Target is not EffectTargetPlayer playerTarget)
                return ResolvedImpactValue.Empty(kind, item.ActionType);
            var playerUpdate =
                playerTarget.Target == ECombatantId.Player
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
            item.Target is not EffectTargetCard cardTarget
            || !frame.CardUpdates.TryGetValue(cardTarget.Target, out var cardUpdate)
        )
            return ResolvedImpactValue.Empty(kind, item.ActionType);

        CombatSimCardAttributeUpdate cardChange;
        if (TryResolveAbilityAttributeType(item, entities, out var expectedAttribute))
        {
            if (
                !IsAttributableCardAttribute(expectedAttribute)
                || !cardUpdate.Attributes.TryGetValue(expectedAttribute, out var expectedChange)
                || expectedChange.Delta == 0
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

    private static bool TryResolveConcurrentConfiguredCardAttributeTransition(
        CombatSimEventEffectExecuted item,
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        CombatSimCardAttributeUpdate cardChange,
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
        CombatSimEventEffectExecuted? unknownClaimant = null;
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
        IReadOnlyList<CombatSimEventEffectExecuted> claimants,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        string? targetId,
        CombatSimCardAttributeUpdate cardChange,
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
                EAttributeModifierOperation.Add => runningValue + operand,
                EAttributeModifierOperation.Subtract => runningValue - operand,
                EAttributeModifierOperation.Multiply => runningValue * operand,
                _ => long.MinValue,
            };
            if (modifier.Operation == EAttributeModifierOperation.Multiply)
                includesMultiply = true;
            if (runningValue is < int.MinValue or > int.MaxValue)
                return false;
        }

        return runningValue == cardChange.CurrentValue;
    }

    private static bool TryResolveEventOrderReplayValue(
        ITValue value,
        string sourceId,
        string targetId,
        ECardAttributeType targetAttribute,
        long runningTargetValue,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        out int configuredValue
    )
    {
        if (value is TFixedValue fixedValue)
            return TryConvertExactInt(fixedValue.Value, out configuredValue);
        if (value is not TReferenceValueCardAttribute reference)
        {
            configuredValue = 0;
            return false;
        }
        if (reference.Target is not TTargetCardSelf)
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
        CombatSimEventEffectExecuted claimant,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        ECardAttributeType expectedAttribute,
        int solvedDelta
    )
    {
        if (
            !TryResolveAbilityAttributeModifier(claimant, entities, out _, out var modifier)
            || modifier.AttributeType != expectedAttribute
        )
            return false;
        if (
            modifier.Value is not TRangeValue range
            || modifier.Operation
                is not (EAttributeModifierOperation.Add or EAttributeModifierOperation.Subtract)
        )
            return true;
        if (
            !TryApplyStaticModifier(range.MinValue, range.Modifier, out var firstBound)
            || !TryApplyStaticModifier(range.MaxValue, range.Modifier, out var secondBound)
        )
            return false;

        var minimum = Math.Min(firstBound, secondBound);
        var maximum = Math.Max(firstBound, secondBound);
        if (modifier.Operation == EAttributeModifierOperation.Subtract)
            (minimum, maximum) = (-maximum, -minimum);
        return solvedDelta >= minimum - 0.0001f && solvedDelta <= maximum + 0.0001f;
    }

    private static bool CanPreserveConcurrentCardAttributeResidual(
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        CombatSimEventEffectExecuted item,
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
                candidate.ActionType == EActionCommandType.CardModifyAttribute
            );
    }

    private static bool TryResolveConfiguredCardAttributeDelta(
        CombatSimEventEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        ECardAttributeType expectedAttribute,
        out int delta,
        out CombatImpactAttributeTransitionFailureReason failureReason
    )
    {
        delta = 0;
        failureReason = default;
        if (effect.ActionType != EActionCommandType.CardModifyAttribute)
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
            is not (EAttributeModifierOperation.Add or EAttributeModifierOperation.Subtract)
        )
        {
            failureReason = modifier.Operation switch
            {
                EAttributeModifierOperation.Multiply =>
                    CombatImpactAttributeTransitionFailureReason.MultiplyOperation,
                EAttributeModifierOperation.AdditiveMultiply =>
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
            modifier.Operation == EAttributeModifierOperation.Subtract
                ? -configuredValue
                : configuredValue;
        return true;
    }

    private static bool TryResolveConfiguredValue(
        ITValue value,
        string sourceId,
        IReadOnlyDictionary<string, Dictionary<ECardAttributeType, int>> cardAttributes,
        out int configuredValue,
        out CombatImpactAttributeTransitionFailureReason failureReason
    )
    {
        failureReason = default;
        switch (value)
        {
            case TFixedValue fixedValue:
                if (TryConvertExactInt(fixedValue.Value, out configuredValue))
                    return true;
                failureReason =
                    CombatImpactAttributeTransitionFailureReason.NonIntegralConfiguredValue;
                return false;
            case TReferenceValueCardAttribute reference:
                if (reference.Target is not TTargetCardSelf)
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
            case TRangeValue:
                configuredValue = 0;
                failureReason = CombatImpactAttributeTransitionFailureReason.RangeValue;
                return false;
            case TReferenceValueCardAttributeUnscaled:
                configuredValue = 0;
                failureReason = CombatImpactAttributeTransitionFailureReason.CardAttributeUnscaled;
                return false;
            case TReferenceValueAttributeChange:
                configuredValue = 0;
                failureReason =
                    CombatImpactAttributeTransitionFailureReason.AttributeChangeReference;
                return false;
            case TReferenceValuePlayerAttribute:
                configuredValue = 0;
                failureReason =
                    CombatImpactAttributeTransitionFailureReason.PlayerAttributeReference;
                return false;
            case TReferenceValueCardCount:
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
        TValueModifier? modifier,
        out float modifiedValue
    )
    {
        if (modifier == null)
        {
            modifiedValue = originalValue;
            return true;
        }
        if (modifier.Value is not TFixedValue)
        {
            modifiedValue = 0;
            return false;
        }

        // The fixed operand is the only reference modifier we can evaluate without live game
        // context. Delegate its rounding and divide-by-zero semantics to the native value type.
        modifiedValue = modifier.GetModifiedValue(originalValue, default);
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
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
        CombatSimEventEffectExecuted item,
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
        CombatSimEventEffectExecuted effect,
        ImpactTransitionClaim transition,
        IReadOnlyDictionary<string, CombatImpactEntity>? entities
    )
    {
        if (
            entities != null
            && effect.ActionType == EActionCommandType.CardModifyAttribute
            && transition.Domain == ImpactTransitionDomain.CardAttribute
        )
        {
            return !TryResolveAbilityAttributeType(effect, entities, out var attributeType)
                || (int)attributeType == transition.Attribute;
        }

        return ClaimsTransition(effect.ActionType, transition);
    }

    private static ResolvedImpactValue? ResolveHealthAdjustment(
        CombatSimPlayerUpdate update,
        CombatImpactKind kind
    )
    {
        var matches = update
            .HealthAdjustments.Where(adjustment => Matches(kind, adjustment))
            .ToArray();
        return ResolveHealthAdjustments(matches, kind);
    }

    private static bool TryResolveConcurrentHealthAdjustment(
        CombatSimFrame frame,
        CombatSimEventEffectExecuted item,
        CombatImpactKind kind,
        IReadOnlyList<CombatSimEventEffectExecuted> executed,
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
            || item.Target is not EffectTargetPlayer playerTarget
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
            playerTarget.Target == ECombatantId.Player
                ? frame.PlayerUpdates
                : frame.OpponentUpdates;
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
        IReadOnlyList<CombatSimPlayerHealthAdjustment> adjustments,
        int effectCount,
        int requestedEffectIndex
    )
    {
        const int maximumCandidates = 1_024;
        var candidates = new List<ResolvedImpactValue>();
        var partition = new List<IReadOnlyList<CombatSimPlayerHealthAdjustment>>(effectCount);
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
        CombatSimPlayerHealthAdjustment first,
        CombatSimPlayerHealthAdjustment second
    ) =>
        first.AttributeChanged == EPlayerHealthChangeType.Health
        && second.AttributeChanged == EPlayerHealthChangeType.Shield
        && first.IsCrit == second.IsCrit;

    private static ResolvedImpactValue? ResolveHealthAdjustments(
        IReadOnlyList<CombatSimPlayerHealthAdjustment> matches,
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

    private static ResolvedImpactValue? ResolveShieldRemoval(CombatSimPlayerUpdate update)
    {
        var matches = update
            .HealthAdjustments.Where(adjustment =>
                adjustment.DamageType == EDamageType.Shield
                && adjustment.AttributeChanged == EPlayerHealthChangeType.Shield
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

    private static bool MatchesExpectedPlayerDelta(EActionCommandType action, int delta)
    {
        var isDecrease =
            action == EActionCommandType.PlayerMaxHealthDecrease || IsPlayerRemovalAction(action);
        return isDecrease ? delta < 0 : delta > 0;
    }

    private static bool IsPlayerRemovalAction(EActionCommandType action) =>
        OptionalCombatTempoTypes.IsRemoveAction(action)
        || action
            is EActionCommandType.PlayerBurnRemove
                or EActionCommandType.PlayerPoisonRemove
                or EActionCommandType.PlayerRegenRemove
                or EActionCommandType.PlayerShieldRemove
                or EActionCommandType.PlayerRageRemove;

    private static int SaturatingInt(long value) =>
        value > int.MaxValue ? int.MaxValue
        : value < int.MinValue ? int.MinValue
        : (int)value;

    private static bool Matches(
        CombatImpactKind kind,
        CombatSimPlayerHealthAdjustment adjustment
    ) =>
        kind switch
        {
            CombatImpactKind.DirectDamage => adjustment.DamageType == EDamageType.Damage
                && adjustment.Amount < 0,
            CombatImpactKind.Healing => adjustment.DamageType == EDamageType.Heal
                && adjustment.AttributeChanged == EPlayerHealthChangeType.Health
                && adjustment.Amount > 0,
            CombatImpactKind.Shield => adjustment.DamageType == EDamageType.Shield
                && adjustment.AttributeChanged == EPlayerHealthChangeType.Shield
                && adjustment.Amount > 0,
            _ => false,
        };

    private static string NativeKeyForAction(EActionCommandType action, string fallback)
    {
        if (OptionalCombatTempoTypes.IsApplyAction(action))
            return "TempoApplyAmount";
        if (OptionalCombatTempoTypes.IsRemoveAction(action))
            return "TempoRemoveAmount";

        return action switch
        {
            EActionCommandType.CardReload => "ReloadAmount",
            EActionCommandType.CardForceUse => "ForceUseTargets",
            EActionCommandType.CardEnchant => "EnchantTargets",
            EActionCommandType.CardEnchantRemove => "EnchantRemoveTargets",
            EActionCommandType.CardTransform or EActionCommandType.CardTransformDestroyed =>
                "TransformTargets",
            EActionCommandType.CardUpgrade => "UpgradeTargets",
            EActionCommandType.CardRepair => "RepairTargets",
            EActionCommandType.CardModifyAttribute => "CardModifyAttribute",
            EActionCommandType.PlayerModifyAttribute => "PlayerModifyAttribute",
            EActionCommandType.PlayerBurnApply => CombatImpactAggregator.NativeKey(
                CombatImpactKind.Burn
            ),
            EActionCommandType.PlayerPoisonApply => CombatImpactAggregator.NativeKey(
                CombatImpactKind.Poison
            ),
            EActionCommandType.PlayerBurnRemove => "BurnRemoveAmount",
            EActionCommandType.PlayerPoisonRemove => "PoisonRemoveAmount",
            EActionCommandType.PlayerRegenApply => "RegenApplyAmount",
            EActionCommandType.PlayerRegenRemove => "RegenRemoveAmount",
            EActionCommandType.PlayerRageApply => "RageApplyAmount",
            EActionCommandType.PlayerRageRemove => "RageRemoveAmount",
            EActionCommandType.PlayerShieldRemove => "ShieldRemoveAmount",
            EActionCommandType.PlayerMaxHealthIncrease => "HealthMaxIncrease",
            EActionCommandType.PlayerMaxHealthDecrease => "HealthMaxDecrease",
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
        IReadOnlyDictionary<ECardStats, int> stats,
        ECardStats stat,
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
        IReadOnlyDictionary<ECardStats, int> stats,
        ECardStats stat,
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

        internal static ResolvedImpactValue Empty(
            CombatImpactKind kind,
            EActionCommandType action
        ) =>
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
        CombatSimCardAttributeUpdate Change,
        string? DirectSourceId,
        string? TriggerSourceId,
        TriggerProvenance Provenance
    );

    private readonly record struct AuraPlayerAttributeCandidate(
        string SourceId,
        string TargetId,
        CombatSimPlayerAttributeUpdate Change,
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
        EActionCommandType ActionType,
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

    private readonly record struct CriticalTriggerExecutionEvidence(
        string TriggerSourceId,
        int FrameIndex,
        EEffectPriority Priority,
        string ListenerSourceId,
        string EffectId,
        string? ExecutionContextId
    );

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
