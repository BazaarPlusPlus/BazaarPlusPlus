#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class PeriodicEffectAttribution
{
    internal const string ModelVersion = "periodic-impact-v7";

    // The shipped model is deliberately a small accounting pass:
    // 1. book typed Burn/Poison Health and Burn Shield adjustments as actual pool movement;
    //    on a death-event frame, Health damage is capped at the remaining positive Health;
    //    contiguous Burn Health/Shield adjustments are one atomic tick, so a lethal Burn Health
    //    component does not discard its paired Shield consumption;
    // 2. reconstruct accepted Regen through the ordered Health/MaxHealth ledger;
    //    on a death-event frame, Regen after Health is already non-positive is not realized;
    // 3. split each measured result by the status stack currently owned by each source.
    // Tick formulas remain corpus diagnostics and never decide whether a typed adjustment is booked.
    //
    // Model invariants. These constrain what may be booked at all; they are not enforced by any
    // test, because they are properties of the model rather than of one output:
    // - Three ledgers stay coupled and their units stay distinct: status stock (per combatant ×
    //   status × epoch), health impact (per combatant × damage type × frame), and shield (per
    //   combatant × frame). Reconciling only one of them cannot establish Exact.
    // - Poison debits Health only. Any Poison-attributed Shield consumption is an invariant
    //   violation, not another supported pool. Burn may consume Shield, but consumed Shield points
    //   and prevented Burn damage are not assumed numerically equal.
    // - Only movement that changed live combat state is realized impact. Overheal, overkill, and
    //   prevented damage are counterfactual and stay unbooked.
    // - EPlayerHealthChangeType.Joy survives in the shared enum for schema compatibility. Enum
    //   presence is not evidence that Joy is a current gameplay pool, so the model does not book it.
    // - CombatImpactPeriodicProof is two-valued on purpose. A tick exposes only aggregate status
    //   and pool movement, so once two resolved owners coexist, moving one unit between them
    //   yields a second feasible allocation — no third "narrowed but not unique" class is
    //   observable here. That is why the earlier Constrained member was removed rather than left
    //   permanently empty, and why a gap of this kind is not answered with a constraint solver.
    // Initial Regen with no tick provenance may be redistributed only across item/skill sources
    // that the completed combat independently proves applied Regen through an execution or
    // CardStats. A static RegenApplyAmount describes capability, not observed contribution. The
    // retrospective fallback and every mixed stack retain Proportional proof internally.

    internal static PeriodicAttributionReport Project(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var diagnostics = new PeriodicEffectAttributionDiagnostics();
        var ledgers = new Dictionary<ECombatantId, CombatantLedger>
        {
            [ECombatantId.Player] = BuildLedger(simulation, ECombatantId.Player),
            [ECombatantId.Opponent] = BuildLedger(simulation, ECombatantId.Opponent),
        };
        var measuredTotals = new Dictionary<PeriodicMeasurementKey, PeriodicMeasuredAmounts>();
        var regenFallbackSources = BuildRegenFallbackSources(simulation, entities);

        for (var frameIndex = 0; frameIndex < simulation.Frames.Count; frameIndex++)
        {
            var frame = simulation.Frames[frameIndex];
            foreach (var combatant in new[] { ECombatantId.Player, ECombatantId.Opponent })
            {
                var update =
                    combatant == ECombatantId.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
                var ledger = ledgers[combatant];
                var pools = ReconstructPools(
                    update,
                    ledger.Attributes,
                    frame
                        .Events.OfType<CombatSimEventCombatantDied>()
                        .Any(death => death.CombatantId == combatant)
                );
                ObserveMeasuredTotals(measuredTotals, combatant, pools);

                var reconciledBeforeImpact = new HashSet<PeriodicStatus>();
                foreach (
                    var status in new[]
                    {
                        PeriodicStatus.Burn,
                        PeriodicStatus.Poison,
                        PeriodicStatus.Regen,
                    }
                )
                {
                    if (!ShouldReconcileBeforeImpact(status, pools, ledger, update))
                        continue;
                    ReconcileStatus(frame, update, combatant, status, ledger, simulation, entities);
                    reconciledBeforeImpact.Add(status);
                }

                AttributeFrameImpact(
                    pools,
                    ledger,
                    diagnostics,
                    frameIndex,
                    combatant,
                    simulation,
                    regenFallbackSources[combatant]
                );

                foreach (
                    var status in new[]
                    {
                        PeriodicStatus.Burn,
                        PeriodicStatus.Poison,
                        PeriodicStatus.Regen,
                    }
                )
                {
                    if (reconciledBeforeImpact.Contains(status))
                        continue;
                    ReconcileStatus(frame, update, combatant, status, ledger, simulation, entities);
                }

                ApplyAttributeUpdates(update, ledger.Attributes);
            }
        }

        return new PeriodicAttributionReport(
            BuildSourceImpacts(diagnostics.Allocations),
            diagnostics.Allocations.ToArray(),
            diagnostics.Gaps.ToArray(),
            measuredTotals
        );
    }

    private static IReadOnlyDictionary<
        PeriodicImpactKey,
        CombatImpactPeriodicImpact
    > BuildSourceImpacts(IReadOnlyList<PeriodicAttributionAllocation> allocations) =>
        allocations
            .GroupBy(allocation => new PeriodicImpactKey(
                allocation.SourceId,
                allocation.Combatant,
                allocation.Kind
            ))
            .ToDictionary(
                group => group.Key,
                group => new CombatImpactPeriodicImpact(
                    SaturatingInt(group.Sum(allocation => allocation.HealthAmount)),
                    SaturatingInt(group.Sum(allocation => allocation.ShieldAmount)),
                    group.Any(allocation =>
                        allocation.Proof == CombatImpactPeriodicProof.Proportional
                    )
                        ? CombatImpactPeriodicProof.Proportional
                        : CombatImpactPeriodicProof.Exact,
                    ModelVersion
                )
            );

    private static void ObserveMeasuredTotals(
        IDictionary<PeriodicMeasurementKey, PeriodicMeasuredAmounts> totals,
        ECombatantId combatant,
        PoolResult pools
    )
    {
        AddMeasuredTotal(
            totals,
            new PeriodicMeasurementKey(combatant, CombatImpactPeriodicKind.Burn),
            pools.BurnHealthDamage,
            pools.BurnShieldConsumed
        );
        AddMeasuredTotal(
            totals,
            new PeriodicMeasurementKey(combatant, CombatImpactPeriodicKind.Poison),
            pools.PoisonHealthDamage,
            0
        );
        AddMeasuredTotal(
            totals,
            new PeriodicMeasurementKey(combatant, CombatImpactPeriodicKind.Regen),
            pools.RegenRealized,
            0
        );
    }

    private static void AddMeasuredTotal(
        IDictionary<PeriodicMeasurementKey, PeriodicMeasuredAmounts> totals,
        PeriodicMeasurementKey key,
        long health,
        long shield
    )
    {
        if (health == 0 && shield == 0)
            return;

        totals.TryGetValue(key, out var current);
        totals[key] = new PeriodicMeasuredAmounts(
            current.HealthAmount + health,
            current.ShieldAmount + shield
        );
    }

    private static CombatantLedger BuildLedger(CombatSim simulation, ECombatantId combatant)
    {
        var attributes = new Dictionary<EPlayerAttributeType, int>();
        foreach (var frame in simulation.Frames)
        {
            var update =
                combatant == ECombatantId.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
            if (update == null)
                continue;
            foreach (var (attribute, transition) in update.Attributes)
                attributes.TryAdd(attribute, transition.PreviousValue);
        }

        attributes.TryAdd(EPlayerAttributeType.Burn, 0);
        attributes.TryAdd(EPlayerAttributeType.Poison, 0);
        attributes.TryAdd(EPlayerAttributeType.HealthRegen, 0);
        attributes.TryAdd(EPlayerAttributeType.Shield, 0);
        if (
            !attributes.ContainsKey(EPlayerAttributeType.HealthMax)
            && attributes.TryGetValue(EPlayerAttributeType.Health, out var health)
        )
        {
            attributes[EPlayerAttributeType.HealthMax] = health;
        }

        var ledger = new CombatantLedger(attributes);
        foreach (
            var status in new[] { PeriodicStatus.Burn, PeriodicStatus.Poison, PeriodicStatus.Regen }
        )
        {
            var initial = ledger.Read(Attribute(status));
            if (initial > 0)
            {
                ledger.Ownership[status].AddUnknown(initial, PeriodicUnknownOrigin.InitialState);
            }
        }
        return ledger;
    }

    private static PoolResult ReconstructPools(
        CombatSimPlayerUpdate? update,
        IDictionary<EPlayerAttributeType, int> attributes,
        bool combatantDied
    )
    {
        if (update == null)
            return PoolResult.Empty;

        var burnHealth = Loss(update, EDamageType.Burn, EPlayerHealthChangeType.Health);
        var burnShield = Loss(update, EDamageType.Burn, EPlayerHealthChangeType.Shield);
        var poisonHealth = Loss(update, EDamageType.Poison, EPlayerHealthChangeType.Health);
        var regenAttempted = Gain(update, EDamageType.Regen, EPlayerHealthChangeType.Health);
        var regenRealized = 0L;

        var hasHealthTransition = update.Attributes.TryGetValue(
            EPlayerAttributeType.Health,
            out var healthTransition
        );
        var hasHealthMaxTransition = update.Attributes.TryGetValue(
            EPlayerAttributeType.HealthMax,
            out var healthMaxTransition
        );
        var hasHealthInLedger = attributes.TryGetValue(
            EPlayerAttributeType.Health,
            out var healthFromLedger
        );
        var hasHealthMaxInLedger = attributes.TryGetValue(
            EPlayerAttributeType.HealthMax,
            out var healthMaxFromLedger
        );
        var hasHealth = hasHealthTransition || hasHealthInLedger;
        var hasHealthMax = hasHealthMaxTransition || hasHealthMaxInLedger;
        var health = hasHealthTransition ? healthTransition!.PreviousValue : healthFromLedger;
        var healthMax = hasHealthMaxTransition
            ? healthMaxTransition!.PreviousValue
            : healthMaxFromLedger;
        var healthReconciled = hasHealth && hasHealthMax;
        if (healthReconciled)
        {
            var closingMax = hasHealthMaxTransition ? healthMaxTransition!.CurrentValue : healthMax;
            long cursor = health + (closingMax - healthMax);
            var terminalReached = combatantDied && cursor <= 0;
            var effectiveBurnHealth = 0L;
            var effectiveBurnShield = 0L;
            var effectivePoisonHealth = 0L;
            for (var index = 0; index < update.HealthAdjustments.Count; index++)
            {
                var adjustment = update.HealthAdjustments[index];
                if (
                    adjustment.DamageType == EDamageType.Burn
                    && adjustment.Amount < 0
                    && adjustment.AttributeChanged
                        is EPlayerHealthChangeType.Health
                            or EPlayerHealthChangeType.Shield
                )
                {
                    var groupWasAlive = !combatantDied || !terminalReached;
                    while (
                        index < update.HealthAdjustments.Count
                        && update.HealthAdjustments[index].DamageType == EDamageType.Burn
                        && update.HealthAdjustments[index].Amount < 0
                        && update.HealthAdjustments[index].AttributeChanged
                            is EPlayerHealthChangeType.Health
                                or EPlayerHealthChangeType.Shield
                    )
                    {
                        var burnAdjustment = update.HealthAdjustments[index];
                        if (burnAdjustment.AttributeChanged == EPlayerHealthChangeType.Health)
                        {
                            var loss = -(long)burnAdjustment.Amount;
                            if (groupWasAlive)
                            {
                                effectiveBurnHealth += combatantDied
                                    ? Math.Min(loss, Math.Max(0L, cursor))
                                    : loss;
                            }
                            cursor += burnAdjustment.Amount;
                            if (combatantDied && cursor <= 0)
                                terminalReached = true;
                        }
                        else if (groupWasAlive)
                        {
                            effectiveBurnShield += -(long)burnAdjustment.Amount;
                        }
                        index++;
                    }
                    index--;
                    continue;
                }
                if (adjustment.AttributeChanged != EPlayerHealthChangeType.Health)
                    continue;
                if (adjustment.Amount <= 0)
                {
                    var loss = -(long)adjustment.Amount;
                    var effectiveLoss =
                        combatantDied && !terminalReached ? Math.Min(loss, Math.Max(0L, cursor))
                        : combatantDied ? 0L
                        : loss;
                    if (adjustment.DamageType == EDamageType.Poison)
                        effectivePoisonHealth += effectiveLoss;
                    cursor += adjustment.Amount;
                    if (combatantDied && cursor <= 0)
                        terminalReached = true;
                    continue;
                }

                var accepted = Math.Min(adjustment.Amount, Math.Max(0L, closingMax - cursor));
                cursor += accepted;
                if (
                    adjustment.DamageType == EDamageType.Regen
                    && (!combatantDied || !terminalReached)
                )
                {
                    regenRealized += accepted;
                }
            }

            burnHealth = effectiveBurnHealth;
            burnShield = effectiveBurnShield;
            poisonHealth = effectivePoisonHealth;

            var closingHealth = hasHealthTransition ? healthTransition!.CurrentValue : cursor;
            healthReconciled = cursor == closingHealth;
            attributes[EPlayerAttributeType.Health] = SaturatingInt(closingHealth);
            attributes[EPlayerAttributeType.HealthMax] = closingMax;
        }

        var hasShieldTransition = update.Attributes.TryGetValue(
            EPlayerAttributeType.Shield,
            out var shieldTransition
        );
        var hasShieldInLedger = attributes.TryGetValue(
            EPlayerAttributeType.Shield,
            out var shieldFromLedger
        );
        var shieldReconciled = hasShieldTransition || hasShieldInLedger;
        var shield = hasShieldTransition ? shieldTransition!.PreviousValue : shieldFromLedger;
        if (shieldReconciled)
        {
            long cursor = shield;
            cursor += update
                .HealthAdjustments.Where(adjustment =>
                    adjustment.AttributeChanged == EPlayerHealthChangeType.Shield
                )
                .Sum(adjustment => (long)adjustment.Amount);
            var closingShield = hasShieldTransition ? shieldTransition!.CurrentValue : cursor;
            shieldReconciled = cursor == closingShield;
            attributes[EPlayerAttributeType.Shield] = SaturatingInt(closingShield);
        }

        return new PoolResult(
            healthReconciled,
            burnHealth,
            burnShield,
            poisonHealth,
            regenAttempted,
            regenRealized
        );
    }

    private static void AttributeFrameImpact(
        PoolResult pools,
        CombatantLedger ledger,
        PeriodicEffectAttributionDiagnostics diagnostics,
        int frameIndex,
        ECombatantId combatant,
        CombatSim simulation,
        IReadOnlyList<string> regenFallbackSources
    )
    {
        if (pools.BurnHealthDamage > 0 || pools.BurnShieldConsumed > 0)
        {
            AllocateImpact(
                ledger.Ownership[PeriodicStatus.Burn],
                PeriodicStatus.Burn,
                pools.BurnHealthDamage,
                pools.BurnShieldConsumed,
                diagnostics,
                frameIndex,
                combatant,
                simulation,
                regenFallbackSources
            );
        }

        if (pools.PoisonHealthDamage > 0)
        {
            AllocateImpact(
                ledger.Ownership[PeriodicStatus.Poison],
                PeriodicStatus.Poison,
                pools.PoisonHealthDamage,
                0,
                diagnostics,
                frameIndex,
                combatant,
                simulation,
                regenFallbackSources
            );
        }

        var openingRegen = ledger.Read(EPlayerAttributeType.HealthRegen);
        if (pools.RegenAttempted <= 0)
            return;
        if (!pools.HealthReconciled)
        {
            diagnostics.ObserveGap(
                frameIndex,
                combatant,
                CombatImpactPeriodicKind.Regen,
                pools.RegenRealized,
                0,
                PeriodicUnknownOrigin.HealthLedgerUnreconciled
            );
            return;
        }
        if (openingRegen <= 0)
        {
            if (regenFallbackSources.Count == 0)
            {
                diagnostics.ObserveGap(
                    frameIndex,
                    combatant,
                    CombatImpactPeriodicKind.Regen,
                    pools.RegenRealized,
                    0,
                    PeriodicUnknownOrigin.StatusStateAbsent
                );
                return;
            }
        }
        AllocateImpact(
            ledger.Ownership[PeriodicStatus.Regen],
            PeriodicStatus.Regen,
            pools.RegenRealized,
            0,
            diagnostics,
            frameIndex,
            combatant,
            simulation,
            regenFallbackSources
        );
    }

    private static bool ShouldReconcileBeforeImpact(
        PeriodicStatus status,
        PoolResult pools,
        CombatantLedger ledger,
        CombatSimPlayerUpdate? update
    )
    {
        if (update == null)
            return false;

        var hasMeasuredImpact = status switch
        {
            PeriodicStatus.Burn => pools.BurnHealthDamage > 0 || pools.BurnShieldConsumed > 0,
            PeriodicStatus.Poison => pools.PoisonHealthDamage > 0,
            PeriodicStatus.Regen => pools.RegenAttempted > 0,
            _ => false,
        };
        if (!hasMeasuredImpact)
            return false;

        var attribute = Attribute(status);
        var opening = ledger.Read(attribute);
        if (!update.Attributes.TryGetValue(attribute, out var transition))
            return false;

        if (opening <= 0 && transition.CurrentValue > 0)
            return true;

        return status == PeriodicStatus.Regen
            && pools.RegenAttempted != opening
            && pools.RegenAttempted == transition.CurrentValue;
    }

    private static void AllocateImpact(
        OwnershipLedger ownership,
        PeriodicStatus status,
        long health,
        long shield,
        PeriodicEffectAttributionDiagnostics diagnostics,
        int frameIndex,
        ECombatantId combatant,
        CombatSim simulation,
        IReadOnlyList<string> regenFallbackSources
    )
    {
        var healthShares = ownership
            .SplitImpact(health, out var unknownHealth)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var shieldShares = ownership.SplitImpact(shield, out var unknownShield);
        if (
            status == PeriodicStatus.Regen
            && unknownHealth > 0
            && ownership.UnknownOrigins
                is PeriodicUnknownOrigin.None
                    or PeriodicUnknownOrigin.InitialState
        )
        {
            foreach (var (sourceId, amount) in ownership.SplitAmongKnownSources(unknownHealth))
            {
                if (amount <= 0)
                    continue;
                var existing = healthShares.GetValueOrDefault(sourceId);
                healthShares[sourceId] = new ImpactShare(existing.Amount + amount, true);
                unknownHealth -= amount;
            }
            if (unknownHealth > 0 && regenFallbackSources.Count > 0)
            {
                foreach (
                    var (sourceId, amount) in OwnershipLedger.SplitAmongSources(
                        unknownHealth,
                        regenFallbackSources,
                        sourceId => StatWeight(simulation, sourceId, PeriodicStatus.Regen)
                    )
                )
                {
                    if (amount <= 0)
                        continue;
                    var existing = healthShares.GetValueOrDefault(sourceId);
                    healthShares[sourceId] = new ImpactShare(existing.Amount + amount, true);
                    unknownHealth -= amount;
                }
            }
        }
        if (unknownHealth > 0 || unknownShield > 0)
        {
            diagnostics.ObserveGap(
                frameIndex,
                combatant,
                Kind(status),
                unknownHealth,
                unknownShield,
                ownership.UnknownOrigins == PeriodicUnknownOrigin.None
                    ? PeriodicUnknownOrigin.EmptyLedger
                    : ownership.UnknownOrigins
            );
        }
        foreach (var sourceId in healthShares.Keys.Union(shieldShares.Keys, StringComparer.Ordinal))
        {
            var healthShare = healthShares.GetValueOrDefault(sourceId);
            var shieldShare = shieldShares.GetValueOrDefault(sourceId);
            if (healthShare.Amount == 0 && shieldShare.Amount == 0)
                continue;

            var approximate = healthShare.IsApproximate || shieldShare.IsApproximate;
            diagnostics.ObserveAllocation(
                frameIndex,
                combatant,
                Kind(status),
                sourceId,
                healthShare.Amount,
                shieldShare.Amount,
                approximate
                    ? CombatImpactPeriodicProof.Proportional
                    : CombatImpactPeriodicProof.Exact
            );
        }
    }

    private static void ReconcileStatus(
        CombatSimFrame frame,
        CombatSimPlayerUpdate? update,
        ECombatantId combatant,
        PeriodicStatus status,
        CombatantLedger ledger,
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var attribute = Attribute(status);
        var opening = ledger.Read(attribute);
        var ownership = ledger.Ownership[status];
        var ticked = status switch
        {
            PeriodicStatus.Burn => HasAdjustment(frame, combatant, EDamageType.Burn),
            PeriodicStatus.Poison => HasAdjustment(frame, combatant, EDamageType.Poison),
            PeriodicStatus.Regen => HasAdjustment(frame, combatant, EDamageType.Regen),
            _ => false,
        };
        var nativeDecay =
            status == PeriodicStatus.Burn && ticked && opening > 0
                ? Math.Max(1, (int)Math.Floor(opening * 0.03))
                : 0;
        if (nativeDecay > 0)
            ownership.Remove(nativeDecay);

        var baseline = opening - nativeDecay;
        var closing =
            update != null && update.Attributes.TryGetValue(attribute, out var statusUpdate)
                ? statusUpdate.CurrentValue
                : baseline;
        var netExternalChange = closing - baseline;
        var sources = ResolveApplySources(frame, combatant, status, entities);
        if (netExternalChange > 0)
        {
            ownership.Add(
                netExternalChange,
                sources,
                sourceId => StatWeight(simulation, sourceId, status)
            );
        }
        else if (netExternalChange < 0)
        {
            ownership.Remove(-netExternalChange);
            if (sources.Count > 0)
                ownership.Contaminate();
        }
        else if (sources.Count > 0)
        {
            ownership.Contaminate();
        }

        ownership.ReconcileTotal(closing);
        ledger.Attributes[attribute] = closing;
    }

    private static IReadOnlyList<string> ResolveApplySources(
        CombatSimFrame frame,
        ECombatantId combatant,
        PeriodicStatus status,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var expectedAction = status switch
        {
            PeriodicStatus.Burn => EActionCommandType.PlayerBurnApply,
            PeriodicStatus.Poison => EActionCommandType.PlayerPoisonApply,
            PeriodicStatus.Regen => EActionCommandType.PlayerRegenApply,
            _ => EActionCommandType.None,
        };
        var sources = new List<string>();
        var hasUnresolved = false;
        foreach (var effect in frame.Events.OfType<CombatSimEventEffectExecuted>())
        {
            if (
                effect.ActionType != expectedAction
                || effect.Target is not EffectTargetPlayer player
                || player.Target != combatant
            )
            {
                continue;
            }

            var sourceId = ResolveActivitySource(effect, entities);
            if (sourceId == null)
                hasUnresolved = true;
            else
                sources.Add(sourceId);
        }

        var resolved = sources
            .Distinct(StringComparer.Ordinal)
            .OrderBy(source => source, StringComparer.Ordinal)
            .ToList();
        if (hasUnresolved)
            resolved.Add(OwnershipLedger.UnknownSource);
        return resolved;
    }

    private static string? ResolveActivitySource(
        CombatSimEventEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        foreach (var candidate in new[] { effect.Source?.Value, effect.TriggerSource?.Value })
        {
            if (
                candidate != null
                && entities.TryGetValue(candidate, out var entity)
                && entity.TypeLabel is "Item" or "Skill"
            )
            {
                return candidate;
            }
        }
        return null;
    }

    private static int StatWeight(CombatSim simulation, string sourceId, PeriodicStatus status)
    {
        if (sourceId == OwnershipLedger.UnknownSource)
            return 1;
        if (!simulation.CardStats.TryGetValue(sourceId, out var stats))
            return 1;
        var stat = status switch
        {
            PeriodicStatus.Burn => ECardStats.BurnAdded,
            PeriodicStatus.Poison => ECardStats.PoisonAdded,
            PeriodicStatus.Regen => ECardStats.RegenAdded,
            _ => ECardStats.UseCount,
        };
        return Math.Max(1, stats.GetValueOrDefault(stat));
    }

    private static IReadOnlyDictionary<
        ECombatantId,
        IReadOnlyList<string>
    > BuildRegenFallbackSources(
        CombatSim simulation,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var candidates = new Dictionary<ECombatantId, HashSet<string>>
        {
            [ECombatantId.Player] = new(StringComparer.Ordinal),
            [ECombatantId.Opponent] = new(StringComparer.Ordinal),
        };

        foreach (var frame in simulation.Frames)
        {
            foreach (var combatant in new[] { ECombatantId.Player, ECombatantId.Opponent })
            {
                foreach (
                    var sourceId in ResolveApplySources(
                        frame,
                        combatant,
                        PeriodicStatus.Regen,
                        entities
                    )
                )
                {
                    if (sourceId != OwnershipLedger.UnknownSource)
                        candidates[combatant].Add(sourceId);
                }
            }
        }

        foreach (var (sourceId, stats) in simulation.CardStats)
        {
            if (
                stats.GetValueOrDefault(ECardStats.RegenAdded) <= 0
                || !entities.TryGetValue(sourceId, out var entity)
                || entity.CombatantId is not { } combatant
                || entity.TypeLabel is not ("Item" or "Skill")
            )
            {
                continue;
            }
            candidates[combatant].Add(sourceId);
        }

        return candidates.ToDictionary(
            pair => pair.Key,
            pair =>
                (IReadOnlyList<string>)
                    pair.Value.OrderBy(source => source, StringComparer.Ordinal).ToArray()
        );
    }

    private static bool HasAdjustment(
        CombatSimFrame frame,
        ECombatantId combatant,
        EDamageType type
    )
    {
        var update = combatant == ECombatantId.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
        return update?.HealthAdjustments.Any(adjustment => adjustment.DamageType == type) == true;
    }

    private static void ApplyAttributeUpdates(
        CombatSimPlayerUpdate? update,
        IDictionary<EPlayerAttributeType, int> attributes
    )
    {
        if (update == null)
            return;
        foreach (var (attribute, transition) in update.Attributes)
            attributes[attribute] = transition.CurrentValue;
    }

    private static long Loss(
        CombatSimPlayerUpdate update,
        EDamageType type,
        EPlayerHealthChangeType pool
    ) =>
        -update
            .HealthAdjustments.Where(adjustment =>
                adjustment.DamageType == type
                && adjustment.AttributeChanged == pool
                && adjustment.Amount < 0
            )
            .Sum(adjustment => (long)adjustment.Amount);

    private static long Gain(
        CombatSimPlayerUpdate update,
        EDamageType type,
        EPlayerHealthChangeType pool
    ) =>
        update
            .HealthAdjustments.Where(adjustment =>
                adjustment.DamageType == type
                && adjustment.AttributeChanged == pool
                && adjustment.Amount > 0
            )
            .Sum(adjustment => (long)adjustment.Amount);

    private static EPlayerAttributeType Attribute(PeriodicStatus status) =>
        status switch
        {
            PeriodicStatus.Burn => EPlayerAttributeType.Burn,
            PeriodicStatus.Poison => EPlayerAttributeType.Poison,
            PeriodicStatus.Regen => EPlayerAttributeType.HealthRegen,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static CombatImpactPeriodicKind Kind(PeriodicStatus status) =>
        status switch
        {
            PeriodicStatus.Burn => CombatImpactPeriodicKind.Burn,
            PeriodicStatus.Poison => CombatImpactPeriodicKind.Poison,
            PeriodicStatus.Regen => CombatImpactPeriodicKind.Regen,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static int SaturatingInt(long value) =>
        value > int.MaxValue ? int.MaxValue
        : value < int.MinValue ? int.MinValue
        : (int)value;

    private enum PeriodicStatus
    {
        Burn,
        Poison,
        Regen,
    }

    private sealed class CombatantLedger
    {
        internal CombatantLedger(Dictionary<EPlayerAttributeType, int> attributes)
        {
            Attributes = attributes;
            Ownership = new Dictionary<PeriodicStatus, OwnershipLedger>
            {
                [PeriodicStatus.Burn] = new(),
                [PeriodicStatus.Poison] = new(),
                [PeriodicStatus.Regen] = new(),
            };
        }

        internal Dictionary<EPlayerAttributeType, int> Attributes { get; }

        internal Dictionary<PeriodicStatus, OwnershipLedger> Ownership { get; }

        internal int Read(EPlayerAttributeType attribute) =>
            Attributes.GetValueOrDefault(attribute);
    }

    private sealed class OwnershipLedger
    {
        internal const string UnknownSource = "\0unknown-periodic-source";

        private readonly Dictionary<string, OwnerBalance> _balances = new(StringComparer.Ordinal);
        private PeriodicUnknownOrigin _unknownOrigins;

        internal PeriodicUnknownOrigin UnknownOrigins => _unknownOrigins;

        internal void AddUnknown(long amount, PeriodicUnknownOrigin origin)
        {
            if (amount <= 0)
                return;
            _unknownOrigins |= origin;
            AddBalance(UnknownSource, amount, true);
        }

        internal void Add(long amount, IReadOnlyList<string> sources, Func<string, int> weight)
        {
            if (amount <= 0)
                return;
            if (sources.Count == 0)
            {
                AddUnknown(amount, PeriodicUnknownOrigin.MissingApplySource);
                return;
            }
            if (sources.Count == 1)
            {
                if (sources[0] == UnknownSource)
                    AddUnknown(amount, PeriodicUnknownOrigin.MissingApplySource);
                else
                    AddBalance(sources[0], amount, false);
                return;
            }

            foreach (var (sourceId, share) in Split(amount, sources, source => weight(source)))
            {
                if (sourceId == UnknownSource)
                    AddUnknown(share, PeriodicUnknownOrigin.MissingApplySource);
                else
                    AddBalance(sourceId, share, true);
            }
        }

        internal void Remove(long amount)
        {
            var total = _balances.Values.Sum(balance => balance.Amount);
            if (amount <= 0 || total <= 0)
                return;
            amount = Math.Min(amount, total);
            var keys = _balances.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            var removals = Split(amount, keys, key => _balances[key].Amount);
            var ambiguous = keys.Length > 1;
            foreach (var (key, removal) in removals)
            {
                var balance = _balances[key];
                var remaining = balance.Amount - removal;
                if (remaining <= 0)
                    _balances.Remove(key);
                else
                    _balances[key] = balance with
                    {
                        Amount = remaining,
                        IsApproximate = balance.IsApproximate || ambiguous,
                    };
            }
            if (!_balances.ContainsKey(UnknownSource))
                _unknownOrigins = PeriodicUnknownOrigin.None;
        }

        internal void ReconcileTotal(long expected)
        {
            expected = Math.Max(0, expected);
            var current = _balances.Values.Sum(balance => balance.Amount);
            if (current < expected)
            {
                AddUnknown(expected - current, PeriodicUnknownOrigin.ReconciledStatusDeficit);
            }
            else if (current > expected)
                Remove(current - expected);
        }

        internal void Contaminate()
        {
            var total = _balances.Values.Sum(balance => balance.Amount);
            _balances.Clear();
            _unknownOrigins = PeriodicUnknownOrigin.None;
            AddUnknown(total, PeriodicUnknownOrigin.ContaminatedEpoch);
        }

        internal IReadOnlyDictionary<string, ImpactShare> SplitImpact(
            long amount,
            out long unknownAmount
        )
        {
            var result = new Dictionary<string, ImpactShare>(StringComparer.Ordinal);
            unknownAmount = 0;
            if (amount <= 0)
                return result;
            if (_balances.Count == 0)
            {
                unknownAmount = amount;
                return result;
            }

            var keys = _balances.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            var mixed = keys.Length > 1 || keys.Contains(UnknownSource, StringComparer.Ordinal);
            foreach (var (key, share) in Split(amount, keys, key => _balances[key].Amount))
            {
                if (key == UnknownSource)
                {
                    unknownAmount = share;
                    continue;
                }
                if (share == 0)
                    continue;
                result[key] = new ImpactShare(share, mixed || _balances[key].IsApproximate);
            }
            return result;
        }

        internal IReadOnlyDictionary<string, long> SplitAmongKnownSources(long amount)
        {
            var keys = _balances
                .Keys.Where(key => key != UnknownSource)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            return Split(amount, keys, key => _balances[key].Amount);
        }

        internal static IReadOnlyDictionary<string, long> SplitAmongSources(
            long amount,
            IReadOnlyList<string> sources,
            Func<string, long> weight
        ) => Split(amount, sources, weight);

        private void AddBalance(string sourceId, long amount, bool approximate)
        {
            if (amount <= 0)
                return;
            if (_balances.TryGetValue(sourceId, out var existing))
            {
                _balances[sourceId] = existing with
                {
                    Amount = existing.Amount + amount,
                    IsApproximate = existing.IsApproximate || approximate,
                };
            }
            else
            {
                _balances[sourceId] = new OwnerBalance(amount, approximate);
            }
        }

        private static IReadOnlyDictionary<string, long> Split(
            long amount,
            IReadOnlyList<string> keys,
            Func<string, long> weight
        )
        {
            var result = keys.ToDictionary(key => key, _ => 0L, StringComparer.Ordinal);
            if (amount <= 0 || keys.Count == 0)
                return result;

            var weights = keys.ToDictionary(
                key => key,
                key => Math.Max(0, weight(key)),
                StringComparer.Ordinal
            );
            var totalWeight = weights.Values.Sum();
            if (totalWeight == 0)
            {
                foreach (var key in keys)
                    weights[key] = 1;
                totalWeight = keys.Count;
            }

            long assigned = 0;
            var remainders = new List<(string Key, long Remainder)>();
            foreach (var key in keys)
            {
                var product = amount * weights[key];
                var share = product / totalWeight;
                result[key] = share;
                assigned += share;
                remainders.Add((key, product % totalWeight));
            }

            foreach (
                var remainder in remainders
                    .OrderByDescending(item => item.Remainder)
                    .ThenBy(item => item.Key, StringComparer.Ordinal)
                    .Take((int)(amount - assigned))
            )
            {
                result[remainder.Key]++;
            }
            return result;
        }

        private sealed record OwnerBalance(long Amount, bool IsApproximate);
    }

    private readonly record struct PoolResult(
        bool HealthReconciled,
        long BurnHealthDamage,
        long BurnShieldConsumed,
        long PoisonHealthDamage,
        long RegenAttempted,
        long RegenRealized
    )
    {
        internal static readonly PoolResult Empty = new(true, 0, 0, 0, 0, 0);
    }

    private readonly record struct ImpactShare(long Amount, bool IsApproximate);
}

[Flags]
internal enum PeriodicUnknownOrigin
{
    None = 0,
    InitialState = 1 << 0,
    MissingApplySource = 1 << 1,
    ReconciledStatusDeficit = 1 << 2,
    ContaminatedEpoch = 1 << 3,
    EmptyLedger = 1 << 4,
    HealthLedgerUnreconciled = 1 << 5,
    StatusStateAbsent = 1 << 6,
}

internal readonly record struct PeriodicAttributionGap(
    int FrameIndex,
    ECombatantId Combatant,
    CombatImpactPeriodicKind Kind,
    long HealthAmount,
    long ShieldAmount,
    PeriodicUnknownOrigin Origin
);

internal readonly record struct PeriodicAttributionAllocation(
    int FrameIndex,
    ECombatantId Combatant,
    CombatImpactPeriodicKind Kind,
    string SourceId,
    long HealthAmount,
    long ShieldAmount,
    CombatImpactPeriodicProof Proof
);

internal sealed class PeriodicEffectAttributionDiagnostics
{
    private readonly List<PeriodicAttributionGap> _gaps = [];
    private readonly List<PeriodicAttributionAllocation> _allocations = [];

    internal IReadOnlyList<PeriodicAttributionGap> Gaps => _gaps;
    internal IReadOnlyList<PeriodicAttributionAllocation> Allocations => _allocations;

    internal void ObserveGap(
        int frameIndex,
        ECombatantId combatant,
        CombatImpactPeriodicKind kind,
        long healthAmount,
        long shieldAmount,
        PeriodicUnknownOrigin origin
    ) => _gaps.Add(new(frameIndex, combatant, kind, healthAmount, shieldAmount, origin));

    internal void ObserveAllocation(
        int frameIndex,
        ECombatantId combatant,
        CombatImpactPeriodicKind kind,
        string sourceId,
        long healthAmount,
        long shieldAmount,
        CombatImpactPeriodicProof proof
    ) =>
        _allocations.Add(
            new(frameIndex, combatant, kind, sourceId, healthAmount, shieldAmount, proof)
        );
}

internal readonly record struct PeriodicMeasurementKey(
    ECombatantId Combatant,
    CombatImpactPeriodicKind Kind
);

internal readonly record struct PeriodicMeasuredAmounts(long HealthAmount, long ShieldAmount);

internal sealed class PeriodicAttributionReport
{
    internal PeriodicAttributionReport(
        IReadOnlyDictionary<PeriodicImpactKey, CombatImpactPeriodicImpact> sourceImpacts,
        IReadOnlyList<PeriodicAttributionAllocation> allocations,
        IReadOnlyList<PeriodicAttributionGap> residuals,
        IReadOnlyDictionary<PeriodicMeasurementKey, PeriodicMeasuredAmounts> measuredTotals
    )
    {
        SourceImpacts = sourceImpacts;
        Allocations = allocations;
        Residuals = residuals;
        MeasuredTotals = measuredTotals;
    }

    internal IReadOnlyDictionary<
        PeriodicImpactKey,
        CombatImpactPeriodicImpact
    > SourceImpacts { get; }

    internal IReadOnlyList<PeriodicAttributionAllocation> Allocations { get; }

    internal IReadOnlyList<PeriodicAttributionGap> Residuals { get; }

    internal IReadOnlyDictionary<
        PeriodicMeasurementKey,
        PeriodicMeasuredAmounts
    > MeasuredTotals { get; }
}
