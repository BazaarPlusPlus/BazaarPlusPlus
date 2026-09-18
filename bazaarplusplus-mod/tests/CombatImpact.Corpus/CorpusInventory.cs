#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Game.PostCombatImpact.Data;

internal sealed record ReplayObservationInput(string BattleId, GameSim Spawn, CombatSim Combat);

internal static class CorpusInventory
{
    private static readonly EPlayerAttributeType[] TrackedAttributes =
    [
        EPlayerAttributeType.Burn,
        EPlayerAttributeType.Poison,
        EPlayerAttributeType.HealthRegen,
        EPlayerAttributeType.Health,
        EPlayerAttributeType.HealthMax,
        EPlayerAttributeType.Shield,
        EPlayerAttributeType.FlatDamageReduction,
        EPlayerAttributeType.PercentDamageReduction,
    ];

    internal static CorpusInventoryReport Build(
        IReadOnlyList<ReplayObservationInput> replays,
        IReadOnlyList<string> invalidPayloads,
        CardAttributeAttributionCorpusReport cardAttributeAttribution
    )
    {
        var adjustments = new SortedDictionary<string, AdjustmentAggregate>(StringComparer.Ordinal);
        var transitions = new SortedDictionary<string, TransitionAggregate>(StringComparer.Ordinal);
        var actions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var openingActions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var openingActionSources = new List<OpeningActionObservation>();
        var periodicFrames = new List<PeriodicFrameObservation>();
        var battleSummaries = new List<BattleInventorySummary>();
        var missingStatSourceIdentities = new List<string>();
        var unresolvedActionSources = new List<string>();
        var multiSourceBattles = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["Burn"] = 0,
            ["Poison"] = 0,
            ["Regen"] = 0,
        };
        var scenarios = new ScenarioCoverage();
        var attribution = new AttributionCoverage();
        var terminalImpact = new TerminalImpactCoverage();
        var resolvedActions = 0;
        var unresolvedActions = 0;
        var totalFrames = 0;
        var periodicAdjustmentCount = 0;

        foreach (var replay in replays.OrderBy(replay => replay.BattleId, StringComparer.Ordinal))
        {
            totalFrames += replay.Combat.Frames.Count;
            var identities = BuildIdentityMap(replay);
            ReadOpeningActions(replay, identities, openingActions, openingActionSources);
            var attributionReport = PeriodicEffectAttribution.Project(
                replay.Combat,
                BuildAttributionEntities(replay, identities)
            );
            attribution.Observe(attributionReport, replay.Combat, identities, replay.Spawn);
            terminalImpact.Observe(replay.Combat, replay.Spawn);
            var statSources = CountStatSources(replay.Combat);
            var statAmounts = SumStatAmounts(replay.Combat);
            foreach (var (status, count) in statSources)
            {
                if (count > 1)
                    multiSourceBattles[status]++;
            }

            var battleMissingIdentities = FindMissingStatSourceIdentities(replay, identities);
            missingStatSourceIdentities.AddRange(battleMissingIdentities);
            var states = CreateStateMaps(replay);
            var battlePeriodicFrames = 0;

            for (var frameIndex = 0; frameIndex < replay.Combat.Frames.Count; frameIndex++)
            {
                var frame = replay.Combat.Frames[frameIndex];
                AddTransformIdentities(frame, identities);
                foreach (var combatant in new[] { ECombatantId.Player, ECombatantId.Opponent })
                {
                    var update =
                        combatant == ECombatantId.Player
                            ? frame.PlayerUpdates
                            : frame.OpponentUpdates;
                    var before = Snapshot(states[combatant]);
                    AddTransitions(update, transitions);
                    ApplyUpdates(states[combatant], update);
                    var after = Snapshot(states[combatant]);
                    var frameAdjustments = ReadAdjustments(update, adjustments);
                    var framePeriodicAdjustmentCount = frameAdjustments.Count(adjustment =>
                        IsPeriodic(adjustment.DamageType)
                    );
                    periodicAdjustmentCount += framePeriodicAdjustmentCount;
                    var frameActions = ReadActions(
                        replay.BattleId,
                        frameIndex,
                        combatant,
                        frame,
                        update,
                        identities,
                        actions,
                        unresolvedActionSources,
                        ref resolvedActions,
                        ref unresolvedActions
                    );

                    if (
                        framePeriodicAdjustmentCount == 0
                        && frameActions.Count == 0
                        && !HasPeriodicTransition(update)
                    )
                    {
                        continue;
                    }

                    var observation = new PeriodicFrameObservation(
                        replay.BattleId,
                        frameIndex,
                        combatant.ToString(),
                        before,
                        after,
                        frameAdjustments,
                        frameActions,
                        ReadContextActions(frame, combatant, identities),
                        frame
                            .Events.OfType<CombatSimEventCombatantDied>()
                            .Any(death => death.CombatantId == combatant)
                    );
                    periodicFrames.Add(observation);
                    battlePeriodicFrames++;
                    scenarios.Observe(observation);
                }
            }

            battleSummaries.Add(
                new BattleInventorySummary(
                    replay.BattleId,
                    replay.Combat.Frames.Count,
                    battlePeriodicFrames,
                    statSources,
                    statAmounts,
                    battleMissingIdentities.Count
                )
            );
        }

        return new CorpusInventoryReport(
            SchemaVersion: 5,
            Battles: replays.Count,
            Frames: totalFrames,
            InvalidPayloads: invalidPayloads.Order(StringComparer.Ordinal).ToArray(),
            PeriodicAdjustmentCount: periodicAdjustmentCount,
            Adjustments: adjustments,
            StatusTransitions: transitions,
            Actions: actions,
            OpeningActions: openingActions,
            OpeningActionSources: openingActionSources
                .OrderBy(action => action.BattleId, StringComparer.Ordinal)
                .ThenBy(action => action.Action, StringComparer.Ordinal)
                .ThenBy(action => action.SourceId, StringComparer.Ordinal)
                .ToArray(),
            Sources: new SourceCoverage(
                resolvedActions,
                unresolvedActions,
                missingStatSourceIdentities.Order(StringComparer.Ordinal).ToArray(),
                unresolvedActionSources.Order(StringComparer.Ordinal).ToArray(),
                multiSourceBattles
            ),
            Scenarios: scenarios,
            Rules: RuleCandidateCoverage.Build(periodicFrames),
            TerminalImpact: terminalImpact,
            Attribution: attribution,
            CardAttributeAttribution: cardAttributeAttribution,
            BattlesSummary: battleSummaries,
            PeriodicFrames: periodicFrames
        );
    }

    private static Dictionary<string, CardIdentity> BuildIdentityMap(ReplayObservationInput replay)
    {
        var identities = new Dictionary<string, CardIdentity>(StringComparer.Ordinal);
        foreach (var (instanceId, card) in replay.Spawn.Cards)
        {
            identities[instanceId] = new CardIdentity(
                TemplateId: null,
                InferCardType(instanceId),
                card.Placement?.Owner
            );
        }
        foreach (var instanceId in replay.Combat.CardStats.Keys)
        {
            var inferredType = InferCardType(instanceId);
            if (inferredType.HasValue && !identities.ContainsKey(instanceId))
                identities[instanceId] = new CardIdentity(null, inferredType, null);
        }
        foreach (var spawned in replay.Spawn.Events.OfType<GameSimEventCardSpawned>())
        {
            identities[spawned.InstanceId] = new CardIdentity(
                spawned.TemplateId,
                spawned.Type,
                spawned.CombatantId
            );
        }

        foreach (var frame in replay.Combat.Frames)
            AddTransformIdentities(frame, identities);
        return identities;
    }

    private static IReadOnlyDictionary<string, CombatImpactEntity> BuildAttributionEntities(
        ReplayObservationInput replay,
        IReadOnlyDictionary<string, CardIdentity> identities
    ) =>
        identities
            .Where(pair => pair.Value.Type is ECardType.Item or ECardType.Skill)
            .ToDictionary(
                pair => pair.Key,
                pair => new CombatImpactEntity(
                    pair.Key,
                    pair.Value.TemplateId ?? pair.Key,
                    pair.Value.Type!.Value.ToString(),
                    null,
                    0,
                    Attributes: replay.Spawn.Cards.TryGetValue(pair.Key, out var card)
                        ? card.Attributes.ToDictionary(
                            attribute => attribute.Key,
                            attribute => attribute.Value.Value
                        )
                        : null,
                    CombatantId: pair.Value.Combatant
                ),
                StringComparer.Ordinal
            );

    private static void ReadOpeningActions(
        ReplayObservationInput replay,
        IReadOnlyDictionary<string, CardIdentity> identities,
        IDictionary<string, int> counts,
        ICollection<OpeningActionObservation> observations
    )
    {
        foreach (var effect in replay.Spawn.Events.OfType<GameSimEventEffectExecuted>())
        {
            if (
                effect.ActionType
                is not (
                    EActionCommandType.PlayerBurnApply
                    or EActionCommandType.PlayerPoisonApply
                    or EActionCommandType.PlayerRegenApply
                )
            )
            {
                continue;
            }

            var action = effect.ActionType.ToString();
            counts.TryGetValue(action, out var count);
            counts[action] = count + 1;
            observations.Add(
                new OpeningActionObservation(
                    replay.BattleId,
                    action,
                    ResolveOpeningSource(effect, identities),
                    effect.Source?.Value,
                    effect.TriggerSource?.Value,
                    effect.Target is EffectTargetPlayer player ? player.Target.ToString() : null
                )
            );
        }
    }

    private static string? ResolveOpeningSource(
        GameSimEventEffectExecuted effect,
        IReadOnlyDictionary<string, CardIdentity> identities
    )
    {
        foreach (var candidate in new[] { effect.Source?.Value, effect.TriggerSource?.Value })
        {
            if (
                candidate != null
                && identities.TryGetValue(candidate, out var identity)
                && identity.Type is ECardType.Item or ECardType.Skill
            )
            {
                return candidate;
            }
        }
        return null;
    }

    private static ECardType? InferCardType(string instanceId) =>
        instanceId.StartsWith("itm_", StringComparison.Ordinal) ? ECardType.Item
        : instanceId.StartsWith("skl_", StringComparison.Ordinal) ? ECardType.Skill
        : null;

    private static void AddTransformIdentities(
        CombatSimFrame frame,
        IDictionary<string, CardIdentity> identities
    )
    {
        foreach (var transformed in frame.Events.OfType<CombatSimEventCardTransformed>())
        {
            foreach (var card in transformed.TransformedCards)
            {
                identities[card.InstanceId] = new CardIdentity(
                    card.TemplateId,
                    card.Type,
                    card.CombatantId
                );
            }
        }

        foreach (var reverted in frame.Events.OfType<CombatSimEventCardTransformReverted>())
        {
            var card = reverted.OriginalCard;
            identities[card.InstanceId] = new CardIdentity(
                card.TemplateId,
                card.Type,
                card.CombatantId
            );
        }
    }

    private static SortedDictionary<string, int> CountStatSources(CombatSim combat)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["Burn"] = 0,
            ["Poison"] = 0,
            ["Regen"] = 0,
        };
        foreach (var stats in combat.CardStats.Values)
        {
            if (stats.GetValueOrDefault(ECardStats.BurnAdded) != 0)
                result["Burn"]++;
            if (stats.GetValueOrDefault(ECardStats.PoisonAdded) != 0)
                result["Poison"]++;
            if (stats.GetValueOrDefault(ECardStats.RegenAdded) != 0)
                result["Regen"]++;
        }
        return result;
    }

    private static SortedDictionary<string, long> SumStatAmounts(CombatSim combat)
    {
        var result = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["Burn"] = 0,
            ["Poison"] = 0,
            ["Regen"] = 0,
        };
        foreach (var stats in combat.CardStats.Values)
        {
            result["Burn"] += stats.GetValueOrDefault(ECardStats.BurnAdded);
            result["Poison"] += stats.GetValueOrDefault(ECardStats.PoisonAdded);
            result["Regen"] += stats.GetValueOrDefault(ECardStats.RegenAdded);
        }
        return result;
    }

    private static List<string> FindMissingStatSourceIdentities(
        ReplayObservationInput replay,
        IReadOnlyDictionary<string, CardIdentity> identities
    )
    {
        var missing = new List<string>();
        foreach (
            var (sourceId, stats) in replay.Combat.CardStats.OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal
            )
        )
        {
            foreach (
                var (stat, label) in new[]
                {
                    (ECardStats.BurnAdded, "Burn"),
                    (ECardStats.PoisonAdded, "Poison"),
                    (ECardStats.RegenAdded, "Regen"),
                }
            )
            {
                if (stats.GetValueOrDefault(stat) != 0 && !identities.ContainsKey(sourceId))
                    missing.Add($"{replay.BattleId}/{sourceId}/{label}");
            }
        }
        return missing;
    }

    private static Dictionary<ECombatantId, Dictionary<EPlayerAttributeType, int>> CreateStateMaps(
        ReplayObservationInput replay
    )
    {
        var states = new Dictionary<ECombatantId, Dictionary<EPlayerAttributeType, int>>
        {
            [ECombatantId.Player] = new Dictionary<EPlayerAttributeType, int>(
                replay.Spawn.Player.Attributes
            ),
            [ECombatantId.Opponent] = new Dictionary<EPlayerAttributeType, int>(
                replay.Spawn.Opponent.Attributes
            ),
        };

        foreach (var combatant in new[] { ECombatantId.Player, ECombatantId.Opponent })
        {
            var state = states[combatant];
            foreach (var frame in replay.Combat.Frames)
            {
                var update =
                    combatant == ECombatantId.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
                if (update == null)
                    continue;
                foreach (var (attribute, transition) in update.Attributes)
                    state.TryAdd(attribute, transition.PreviousValue);
            }

            state.TryAdd(EPlayerAttributeType.Burn, 0);
            state.TryAdd(EPlayerAttributeType.Poison, 0);
            state.TryAdd(EPlayerAttributeType.HealthRegen, 0);
            state.TryAdd(EPlayerAttributeType.Shield, 0);
            if (
                !state.ContainsKey(EPlayerAttributeType.HealthMax)
                && state.TryGetValue(EPlayerAttributeType.Health, out var health)
            )
            {
                state[EPlayerAttributeType.HealthMax] = health;
            }
        }

        return states;
    }

    private static AttributeSnapshot Snapshot(
        IReadOnlyDictionary<EPlayerAttributeType, int> state
    ) =>
        new(
            Read(state, EPlayerAttributeType.Burn),
            Read(state, EPlayerAttributeType.Poison),
            Read(state, EPlayerAttributeType.HealthRegen),
            Read(state, EPlayerAttributeType.Health),
            Read(state, EPlayerAttributeType.HealthMax),
            Read(state, EPlayerAttributeType.Shield),
            Read(state, EPlayerAttributeType.FlatDamageReduction),
            Read(state, EPlayerAttributeType.PercentDamageReduction)
        );

    private static int? Read(
        IReadOnlyDictionary<EPlayerAttributeType, int> state,
        EPlayerAttributeType attribute
    ) => state.TryGetValue(attribute, out var value) ? value : null;

    private static void ApplyUpdates(
        IDictionary<EPlayerAttributeType, int> state,
        CombatSimPlayerUpdate? update
    )
    {
        if (update == null)
            return;
        foreach (var (attribute, transition) in update.Attributes)
            state[attribute] = transition.CurrentValue;
    }

    private static void AddTransitions(
        CombatSimPlayerUpdate? update,
        IDictionary<string, TransitionAggregate> transitions
    )
    {
        if (update == null)
            return;
        foreach (var attribute in TrackedAttributes.Take(3))
        {
            if (!update.Attributes.TryGetValue(attribute, out var transition))
                continue;
            var direction =
                transition.Delta > 0 ? "Gain"
                : transition.Delta < 0 ? "Loss"
                : "Zero";
            var key = $"{attribute}/{direction}";
            if (!transitions.TryGetValue(key, out var aggregate))
            {
                aggregate = new TransitionAggregate();
                transitions[key] = aggregate;
            }
            aggregate.Count++;
            aggregate.NetDelta += transition.Delta;
            aggregate.AbsoluteDelta += Math.Abs((long)transition.Delta);
        }
    }

    private static List<AdjustmentObservation> ReadAdjustments(
        CombatSimPlayerUpdate? update,
        IDictionary<string, AdjustmentAggregate> aggregates
    )
    {
        var result = new List<AdjustmentObservation>();
        if (update == null)
            return result;
        foreach (var adjustment in update.HealthAdjustments)
        {
            var sign =
                adjustment.Amount > 0 ? "Gain"
                : adjustment.Amount < 0 ? "Loss"
                : "Zero";
            var key = $"{adjustment.DamageType}/{adjustment.AttributeChanged}/{sign}";
            if (!aggregates.TryGetValue(key, out var aggregate))
            {
                aggregate = new AdjustmentAggregate();
                aggregates[key] = aggregate;
            }
            aggregate.Count++;
            aggregate.NetAmount += adjustment.Amount;
            aggregate.AbsoluteAmount += Math.Abs((long)adjustment.Amount);
            if (adjustment.IsCrit)
                aggregate.CriticalCount++;
            if (adjustment.IsDamageReduced)
                aggregate.ReducedCount++;
            result.Add(
                new AdjustmentObservation(
                    adjustment.DamageType.ToString(),
                    adjustment.AttributeChanged.ToString(),
                    adjustment.Amount,
                    adjustment.IsCrit,
                    adjustment.IsDamageReduced
                )
            );
        }
        return result;
    }

    private static List<ActionObservation> ReadActions(
        string battleId,
        int frameIndex,
        ECombatantId combatant,
        CombatSimFrame frame,
        CombatSimPlayerUpdate? update,
        IReadOnlyDictionary<string, CardIdentity> identities,
        IDictionary<string, int> actionCounts,
        ICollection<string> unresolvedActionSources,
        ref int resolvedActions,
        ref int unresolvedActions
    )
    {
        var result = new List<ActionObservation>();
        foreach (var effect in frame.Events.OfType<CombatSimEventEffectExecuted>())
        {
            if (!Targets(effect, combatant) || !IsPeriodicAction(effect.ActionType, update))
                continue;
            var action = effect.ActionType.ToString();
            actionCounts.TryGetValue(action, out var actionCount);
            actionCounts[action] = actionCount + 1;
            var sourceId = ResolveSource(effect, identities);
            if (sourceId == null)
            {
                unresolvedActions++;
                unresolvedActionSources.Add($"{battleId}/{frameIndex}/{combatant}/{action}");
            }
            else
            {
                resolvedActions++;
            }
            result.Add(
                new ActionObservation(
                    action,
                    sourceId,
                    effect.Source?.Value,
                    effect.TriggerSource?.Value
                )
            );
        }
        return result;
    }

    internal static string? ResolveSource(
        CombatSimEventEffectExecuted effect,
        IReadOnlyDictionary<string, CardIdentity> identities
    )
    {
        foreach (var candidate in new[] { effect.Source?.Value, effect.TriggerSource?.Value })
        {
            if (
                candidate != null
                && identities.TryGetValue(candidate, out var identity)
                && identity.Type is ECardType.Item or ECardType.Skill
            )
            {
                return candidate;
            }
        }
        return null;
    }

    private static bool Targets(CombatSimEventEffectExecuted effect, ECombatantId combatant) =>
        effect.Target is EffectTargetPlayer player && player.Target == combatant;

    private static IReadOnlyList<ActionObservation> ReadContextActions(
        CombatSimFrame frame,
        ECombatantId combatant,
        IReadOnlyDictionary<string, CardIdentity> identities
    ) =>
        frame
            .Events.OfType<CombatSimEventEffectExecuted>()
            .Where(effect => Targets(effect, combatant))
            .Select(effect => new ActionObservation(
                effect.ActionType.ToString(),
                ResolveSource(effect, identities),
                effect.Source?.Value,
                effect.TriggerSource?.Value
            ))
            .ToArray();

    private static bool IsPeriodicAction(
        EActionCommandType action,
        CombatSimPlayerUpdate? update
    ) =>
        action
            is EActionCommandType.PlayerBurnApply
                or EActionCommandType.PlayerBurnRemove
                or EActionCommandType.PlayerPoisonApply
                or EActionCommandType.PlayerPoisonRemove
                or EActionCommandType.PlayerRegenApply
                or EActionCommandType.PlayerRegenRemove
        || action == EActionCommandType.PlayerModifyAttribute && HasPeriodicTransition(update);

    private static bool HasPeriodicTransition(CombatSimPlayerUpdate? update) =>
        update != null
        && TrackedAttributes
            .Take(3)
            .Any(attribute =>
                update.Attributes.TryGetValue(attribute, out var transition)
                && transition.Delta != 0
            );

    private static bool IsPeriodic(EDamageType type) =>
        type is EDamageType.Burn or EDamageType.Poison or EDamageType.Regen;

    private static bool IsPeriodic(string type) => type is "Burn" or "Poison" or "Regen";
}

internal sealed record CardIdentity(string? TemplateId, ECardType? Type, ECombatantId? Combatant);

internal sealed record CorpusInventoryReport(
    int SchemaVersion,
    int Battles,
    int Frames,
    IReadOnlyList<string> InvalidPayloads,
    int PeriodicAdjustmentCount,
    IReadOnlyDictionary<string, AdjustmentAggregate> Adjustments,
    IReadOnlyDictionary<string, TransitionAggregate> StatusTransitions,
    IReadOnlyDictionary<string, int> Actions,
    IReadOnlyDictionary<string, int> OpeningActions,
    IReadOnlyList<OpeningActionObservation> OpeningActionSources,
    SourceCoverage Sources,
    ScenarioCoverage Scenarios,
    RuleCandidateCoverage Rules,
    TerminalImpactCoverage TerminalImpact,
    AttributionCoverage Attribution,
    CardAttributeAttributionCorpusReport CardAttributeAttribution,
    IReadOnlyList<BattleInventorySummary> BattlesSummary,
    IReadOnlyList<PeriodicFrameObservation> PeriodicFrames
);

internal sealed class TerminalImpactCoverage
{
    public int DeathEvents { get; private set; }
    public int CombatantsWithMultipleDeathEvents { get; private set; }
    public int FramesAfterFirstDeathEvent { get; private set; }
    public int PositiveHealthFramesAfterFirstDeathEvent { get; private set; }
    public int PeriodicAdjustmentFramesAfterFirstDeathEvent { get; private set; }
    public SortedDictionary<string, long> PeriodicAdjustmentAmountAfterFirstDeathEvent { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> PeriodicHealthLossOnDeathEventFrames { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<
        string,
        long
    > CombatEffectivePeriodicHealthLossOnDeathEventFrames { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> PeriodicHealthOverkillOnDeathEventFrames { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<
        string,
        long
    > ReversedOrderCombatEffectivePeriodicHealthLossOnDeathEventFrames { get; } =
        new(StringComparer.Ordinal);
    public long BurnShieldConsumedOnDeathEventFrames { get; private set; }
    public long CombatEffectiveBurnShieldConsumedOnDeathEventFrames { get; private set; }
    public long BurnShieldConsumedAfterLethalHealthOnDeathEventFrames { get; private set; }
    public long ReversedOrderCombatEffectiveBurnShieldConsumedOnDeathEventFrames
    {
        get;
        private set;
    }
    public long AtomicBurnGroupCombatEffectiveHealthLossOnDeathEventFrames { get; private set; }
    public long AtomicBurnGroupCombatEffectiveShieldConsumedOnDeathEventFrames { get; private set; }

    internal void Observe(CombatSim combat, GameSim spawn)
    {
        foreach (var combatant in new[] { ECombatantId.Player, ECombatantId.Opponent })
        {
            var state = new Dictionary<EPlayerAttributeType, int>(
                combatant == ECombatantId.Player
                    ? spawn.Player.Attributes
                    : spawn.Opponent.Attributes
            );
            foreach (var frame in combat.Frames)
            {
                var update =
                    combatant == ECombatantId.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
                if (update == null)
                    continue;
                foreach (var (attribute, transition) in update.Attributes)
                    state.TryAdd(attribute, transition.PreviousValue);
            }
            if (
                !state.ContainsKey(EPlayerAttributeType.HealthMax)
                && state.TryGetValue(EPlayerAttributeType.Health, out var initialHealth)
            )
            {
                state[EPlayerAttributeType.HealthMax] = initialHealth;
            }

            var deathEvents = 0;
            for (var frameIndex = 0; frameIndex < combat.Frames.Count; frameIndex++)
            {
                var frame = combat.Frames[frameIndex];
                var update =
                    combatant == ECombatantId.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
                var hasDeathEvent = frame
                    .Events.OfType<CombatSimEventCombatantDied>()
                    .Any(death => death.CombatantId == combatant);

                if (deathEvents > 0)
                {
                    FramesAfterFirstDeathEvent++;
                    if (
                        state.GetValueOrDefault(EPlayerAttributeType.Health) > 0
                        || (
                            update?.Attributes.TryGetValue(
                                EPlayerAttributeType.Health,
                                out var postDeathHealth
                            ) == true
                            && postDeathHealth.CurrentValue > 0
                        )
                    )
                    {
                        PositiveHealthFramesAfterFirstDeathEvent++;
                    }

                    var postDeathPeriodicAdjustments =
                        update
                            ?.HealthAdjustments.Where(adjustment =>
                                adjustment.DamageType
                                    is EDamageType.Burn
                                        or EDamageType.Poison
                                        or EDamageType.Regen
                            )
                            .ToArray()
                        ?? [];
                    if (postDeathPeriodicAdjustments.Length > 0)
                    {
                        PeriodicAdjustmentFramesAfterFirstDeathEvent++;
                        foreach (var adjustment in postDeathPeriodicAdjustments)
                        {
                            var key = $"{adjustment.DamageType}/{adjustment.AttributeChanged}";
                            PeriodicAdjustmentAmountAfterFirstDeathEvent[key] =
                                PeriodicAdjustmentAmountAfterFirstDeathEvent.GetValueOrDefault(key)
                                + Math.Abs((long)adjustment.Amount);
                        }
                    }
                }

                if (
                    update != null
                    && state.TryGetValue(EPlayerAttributeType.Health, out var openingHealth)
                    && state.TryGetValue(EPlayerAttributeType.HealthMax, out var openingMax)
                )
                {
                    var closingMax = update.Attributes.TryGetValue(
                        EPlayerAttributeType.HealthMax,
                        out var maxTransition
                    )
                        ? maxTransition.CurrentValue
                        : openingMax;
                    long health = openingHealth + (closingMax - openingMax);
                    var terminalReached = hasDeathEvent && health <= 0;
                    foreach (var adjustment in update.HealthAdjustments)
                    {
                        if (adjustment.AttributeChanged != EPlayerHealthChangeType.Health)
                        {
                            if (
                                hasDeathEvent
                                && adjustment.DamageType == EDamageType.Burn
                                && adjustment.AttributeChanged == EPlayerHealthChangeType.Shield
                                && adjustment.Amount < 0
                            )
                            {
                                var consumed = -(long)adjustment.Amount;
                                BurnShieldConsumedOnDeathEventFrames += consumed;
                                if (!terminalReached)
                                    CombatEffectiveBurnShieldConsumedOnDeathEventFrames += consumed;
                                else
                                    BurnShieldConsumedAfterLethalHealthOnDeathEventFrames +=
                                        consumed;
                            }
                            continue;
                        }
                        if (adjustment.Amount <= 0)
                        {
                            if (
                                hasDeathEvent
                                && adjustment.DamageType is EDamageType.Burn or EDamageType.Poison
                            )
                            {
                                var key = adjustment.DamageType.ToString();
                                var loss = -(long)adjustment.Amount;
                                var effective = terminalReached
                                    ? 0L
                                    : Math.Min(loss, Math.Max(0L, health));
                                PeriodicHealthLossOnDeathEventFrames[key] =
                                    PeriodicHealthLossOnDeathEventFrames.GetValueOrDefault(key)
                                    + loss;
                                CombatEffectivePeriodicHealthLossOnDeathEventFrames[key] =
                                    CombatEffectivePeriodicHealthLossOnDeathEventFrames.GetValueOrDefault(
                                        key
                                    ) + effective;
                                PeriodicHealthOverkillOnDeathEventFrames[key] =
                                    PeriodicHealthOverkillOnDeathEventFrames.GetValueOrDefault(key)
                                    + (loss - effective);
                            }
                            health += adjustment.Amount;
                            if (hasDeathEvent && health <= 0)
                                terminalReached = true;
                            continue;
                        }

                        var accepted = Math.Min(
                            adjustment.Amount,
                            Math.Max(0L, closingMax - health)
                        );
                        health += accepted;
                    }

                    if (hasDeathEvent)
                    {
                        long reversedHealth = openingHealth + (closingMax - openingMax);
                        var reversedTerminalReached = reversedHealth <= 0;
                        foreach (
                            var adjustment in update.HealthAdjustments.AsEnumerable().Reverse()
                        )
                        {
                            if (adjustment.AttributeChanged != EPlayerHealthChangeType.Health)
                            {
                                if (
                                    adjustment.DamageType == EDamageType.Burn
                                    && adjustment.AttributeChanged == EPlayerHealthChangeType.Shield
                                    && adjustment.Amount < 0
                                    && !reversedTerminalReached
                                )
                                {
                                    ReversedOrderCombatEffectiveBurnShieldConsumedOnDeathEventFrames +=
                                        -(long)adjustment.Amount;
                                }
                                continue;
                            }
                            if (adjustment.Amount <= 0)
                            {
                                if (adjustment.DamageType is EDamageType.Burn or EDamageType.Poison)
                                {
                                    var key = adjustment.DamageType.ToString();
                                    var loss = -(long)adjustment.Amount;
                                    var effective = reversedTerminalReached
                                        ? 0L
                                        : Math.Min(loss, Math.Max(0L, reversedHealth));
                                    ReversedOrderCombatEffectivePeriodicHealthLossOnDeathEventFrames[
                                        key
                                    ] =
                                        ReversedOrderCombatEffectivePeriodicHealthLossOnDeathEventFrames.GetValueOrDefault(
                                            key
                                        ) + effective;
                                }
                                reversedHealth += adjustment.Amount;
                                if (reversedHealth <= 0)
                                    reversedTerminalReached = true;
                                continue;
                            }

                            var reversedAccepted = Math.Min(
                                adjustment.Amount,
                                Math.Max(0L, closingMax - reversedHealth)
                            );
                            reversedHealth += reversedAccepted;
                        }

                        long groupedHealth = openingHealth + (closingMax - openingMax);
                        var groupedTerminalReached = groupedHealth <= 0;
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
                                var groupWasAlive = !groupedTerminalReached;
                                while (
                                    index < update.HealthAdjustments.Count
                                    && update.HealthAdjustments[index].DamageType
                                        == EDamageType.Burn
                                    && update.HealthAdjustments[index].Amount < 0
                                    && update.HealthAdjustments[index].AttributeChanged
                                        is EPlayerHealthChangeType.Health
                                            or EPlayerHealthChangeType.Shield
                                )
                                {
                                    var burnAdjustment = update.HealthAdjustments[index];
                                    if (
                                        burnAdjustment.AttributeChanged
                                        == EPlayerHealthChangeType.Health
                                    )
                                    {
                                        var loss = -(long)burnAdjustment.Amount;
                                        if (groupWasAlive)
                                        {
                                            AtomicBurnGroupCombatEffectiveHealthLossOnDeathEventFrames +=
                                                Math.Min(loss, Math.Max(0L, groupedHealth));
                                        }
                                        groupedHealth += burnAdjustment.Amount;
                                        if (groupedHealth <= 0)
                                            groupedTerminalReached = true;
                                    }
                                    else if (groupWasAlive)
                                    {
                                        AtomicBurnGroupCombatEffectiveShieldConsumedOnDeathEventFrames +=
                                            -(long)burnAdjustment.Amount;
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
                                groupedHealth += adjustment.Amount;
                                if (groupedHealth <= 0)
                                    groupedTerminalReached = true;
                                continue;
                            }

                            var groupedAccepted = Math.Min(
                                adjustment.Amount,
                                Math.Max(0L, closingMax - groupedHealth)
                            );
                            groupedHealth += groupedAccepted;
                        }
                    }
                }

                if (update != null)
                {
                    foreach (var (attribute, transition) in update.Attributes)
                        state[attribute] = transition.CurrentValue;
                }
                if (hasDeathEvent)
                {
                    DeathEvents++;
                    deathEvents++;
                }
            }
            if (deathEvents > 1)
                CombatantsWithMultipleDeathEvents++;
        }
    }
}

internal sealed record OpeningActionObservation(
    string BattleId,
    string Action,
    string? SourceId,
    string? DirectSourceId,
    string? TriggerSourceId,
    string? Target
);

internal sealed class AdjustmentAggregate
{
    public int Count { get; set; }
    public long NetAmount { get; set; }
    public long AbsoluteAmount { get; set; }
    public int CriticalCount { get; set; }
    public int ReducedCount { get; set; }
}

internal sealed class TransitionAggregate
{
    public int Count { get; set; }
    public long NetDelta { get; set; }
    public long AbsoluteDelta { get; set; }
}

internal sealed record SourceCoverage(
    int ResolvedActions,
    int UnresolvedActions,
    IReadOnlyList<string> MissingStatSourceIdentities,
    IReadOnlyList<string> UnresolvedActionSources,
    IReadOnlyDictionary<string, int> MultiSourceBattlesByStatus
);

internal sealed class ScenarioCoverage
{
    public int BurnWithoutShield { get; set; }
    public int BurnWithShield { get; set; }
    public int BurnShieldUnknown { get; set; }
    public int BurnHealthAndShieldAdjustment { get; set; }
    public int BurnHealthOnlyAdjustment { get; set; }
    public int BurnShieldOnlyAdjustment { get; set; }
    public int PoisonWhileShieldPresent { get; set; }
    public int PoisonShieldAdjustment { get; set; }
    public int PoisonHealthAdjustment { get; set; }
    public int RegenHealthAdjustment { get; set; }
    public int RegenReachedMaxHealth { get; set; }

    internal void Observe(PeriodicFrameObservation frame)
    {
        var burnHealth = Loss(frame.Adjustments, "Burn", "Health");
        var burnShield = Loss(frame.Adjustments, "Burn", "Shield");
        var poisonHealth = Loss(frame.Adjustments, "Poison", "Health");
        var poisonShield = Loss(frame.Adjustments, "Poison", "Shield");
        var regenHealth = Gain(frame.Adjustments, "Regen", "Health");

        if (burnHealth > 0 || burnShield > 0)
        {
            if (!frame.Before.Shield.HasValue)
                BurnShieldUnknown++;
            else if (frame.Before.Shield.Value > 0)
                BurnWithShield++;
            else
                BurnWithoutShield++;

            if (burnHealth > 0 && burnShield > 0)
                BurnHealthAndShieldAdjustment++;
            else if (burnHealth > 0)
                BurnHealthOnlyAdjustment++;
            else
                BurnShieldOnlyAdjustment++;
        }

        if (poisonHealth > 0)
        {
            PoisonHealthAdjustment++;
            if (frame.Before.Shield > 0)
                PoisonWhileShieldPresent++;
        }
        if (poisonShield > 0)
            PoisonShieldAdjustment++;
        if (regenHealth > 0)
        {
            RegenHealthAdjustment++;
            if (
                frame.After.Health.HasValue
                && frame.After.HealthMax.HasValue
                && frame.After.Health.Value == frame.After.HealthMax.Value
            )
            {
                RegenReachedMaxHealth++;
            }
        }
    }

    private static long Loss(
        IEnumerable<AdjustmentObservation> adjustments,
        string damageType,
        string pool
    ) =>
        -adjustments
            .Where(adjustment =>
                adjustment.DamageType == damageType
                && adjustment.Pool == pool
                && adjustment.Amount < 0
            )
            .Sum(adjustment => (long)adjustment.Amount);

    private static long Gain(
        IEnumerable<AdjustmentObservation> adjustments,
        string damageType,
        string pool
    ) =>
        adjustments
            .Where(adjustment =>
                adjustment.DamageType == damageType
                && adjustment.Pool == pool
                && adjustment.Amount > 0
            )
            .Sum(adjustment => (long)adjustment.Amount);
}

internal sealed record RuleCandidateCoverage(
    CandidateCoverage BurnShieldBridge,
    CandidateCoverage BurnDecay,
    CandidateCoverage PoisonTick,
    CandidateCoverage RegenTick,
    PoolLedgerCoverage HealthLedger,
    PoolLedgerCoverage ShieldLedger,
    ApplicationCoverage Applications,
    RealizedImpactTotals RealizedTotals
)
{
    internal static RuleCandidateCoverage Build(IReadOnlyList<PeriodicFrameObservation> frames)
    {
        var burnBridge = new CandidateCounter();
        var burnDecay = new CandidateCounter();
        var poisonTick = new CandidateCounter();
        var regenTick = new CandidateCounter();
        var healthLedger = new PoolLedgerCounter();
        var shieldLedger = new PoolLedgerCounter();
        var applications = new ApplicationCoverage();
        var realizedTotals = new RealizedImpactTotals();

        foreach (var frame in frames)
        {
            ObserveBurnBridge(frame, burnBridge);
            ObserveBurnDecay(frame, burnDecay);
            ObservePoisonTick(frame, poisonTick);
            ObserveRegenTick(frame, regenTick);
            ObserveHealthLedger(frame, healthLedger);
            ObserveShieldLedger(frame, shieldLedger);
            applications.Observe(frame);
            realizedTotals.Observe(frame);
        }

        return new RuleCandidateCoverage(
            burnBridge.Build(),
            burnDecay.Build(),
            poisonTick.Build(),
            regenTick.Build(),
            healthLedger.Build(),
            shieldLedger.Build(),
            applications,
            realizedTotals
        );
    }

    private static void ObserveBurnBridge(PeriodicFrameObservation frame, CandidateCounter counter)
    {
        var healthDamage = Loss(frame, "Burn", "Health");
        var shieldConsumed = Loss(frame, "Burn", "Shield");
        if (healthDamage == 0 && shieldConsumed == 0)
            return;
        if (!frame.Before.Burn.HasValue || !frame.Before.Shield.HasValue)
        {
            counter.Skip();
            return;
        }

        var burn = frame.Before.Burn.Value;
        var shield = frame.Before.Shield.Value;
        var expectedShield = Math.Min(shield, burn / 2);
        var expectedHealth = Math.Max(0, burn - 2 * shield);
        counter.Observe(
            healthDamage == expectedHealth && shieldConsumed == expectedShield,
            healthDamage + shieldConsumed
        );
    }

    private static void ObserveBurnDecay(PeriodicFrameObservation frame, CandidateCounter counter)
    {
        if (Loss(frame, "Burn", "Health") == 0 && Loss(frame, "Burn", "Shield") == 0)
            return;
        if (
            !frame.Before.Burn.HasValue
            || !frame.After.Burn.HasValue
            || frame.Before.Burn.Value <= 0
        )
        {
            counter.Skip();
            return;
        }
        if (
            frame.Actions.Any(action => action.Action is "PlayerBurnApply" or "PlayerBurnRemove")
            || frame.Adjustments.Any(adjustment => adjustment.DamageType == "Heal")
        )
        {
            counter.Skip();
            return;
        }

        var decay = Math.Max(1, (int)Math.Floor(frame.Before.Burn.Value * 0.03));
        counter.Observe(frame.After.Burn.Value == frame.Before.Burn.Value - decay, decay);
    }

    private static void ObservePoisonTick(PeriodicFrameObservation frame, CandidateCounter counter)
    {
        var damage = Loss(frame, "Poison", "Health");
        if (damage == 0)
            return;
        if (!frame.Before.Poison.HasValue || !frame.After.Poison.HasValue)
        {
            counter.Skip();
            return;
        }

        var expected =
            frame.FrameIndex == 0
            && frame.Before.Poison.Value == 0
            && frame.Actions.Any(action => action.Action == "PlayerPoisonApply")
                ? frame.After.Poison.Value
                : frame.Before.Poison.Value;
        counter.Observe(damage == expected, damage);
    }

    private static void ObserveRegenTick(PeriodicFrameObservation frame, CandidateCounter counter)
    {
        var attempted = Gain(frame, "Regen", "Health");
        if (attempted == 0)
            return;
        if (!frame.Before.Regen.HasValue || frame.Before.Regen.Value <= 0)
        {
            counter.Skip();
            return;
        }

        counter.Observe(attempted == frame.Before.Regen.Value, attempted);
    }

    private static void ObserveHealthLedger(
        PeriodicFrameObservation frame,
        PoolLedgerCounter counter
    )
    {
        if (
            !frame.Before.Health.HasValue
            || !frame.After.Health.HasValue
            || !frame.Before.HealthMax.HasValue
            || !frame.After.HealthMax.HasValue
        )
        {
            counter.Skip();
            return;
        }

        var health =
            frame.Before.Health.Value
            + (frame.After.HealthMax.Value - frame.Before.HealthMax.Value);
        long attemptedGain = 0;
        long realizedGain = 0;
        long loss = 0;
        foreach (
            var adjustment in frame.Adjustments.Where(adjustment => adjustment.Pool == "Health")
        )
        {
            if (adjustment.Amount <= 0)
            {
                health += adjustment.Amount;
                loss += -(long)adjustment.Amount;
                continue;
            }

            attemptedGain += adjustment.Amount;
            var accepted = Math.Min(
                adjustment.Amount,
                Math.Max(0, frame.After.HealthMax.Value - health)
            );
            health += accepted;
            realizedGain += accepted;
        }

        counter.Observe(health == frame.After.Health.Value, attemptedGain, realizedGain, loss);
    }

    private static void ObserveShieldLedger(
        PeriodicFrameObservation frame,
        PoolLedgerCounter counter
    )
    {
        if (!frame.Before.Shield.HasValue || !frame.After.Shield.HasValue)
        {
            counter.Skip();
            return;
        }

        long shield = frame.Before.Shield.Value;
        long gain = 0;
        long loss = 0;
        foreach (
            var adjustment in frame.Adjustments.Where(adjustment => adjustment.Pool == "Shield")
        )
        {
            shield += adjustment.Amount;
            if (adjustment.Amount > 0)
                gain += adjustment.Amount;
            else
                loss += -(long)adjustment.Amount;
        }
        counter.Observe(shield == frame.After.Shield.Value, gain, gain, loss);
    }

    private static long Loss(PeriodicFrameObservation frame, string type, string pool) =>
        -frame
            .Adjustments.Where(adjustment =>
                adjustment.DamageType == type && adjustment.Pool == pool && adjustment.Amount < 0
            )
            .Sum(adjustment => (long)adjustment.Amount);

    private static long Gain(PeriodicFrameObservation frame, string type, string pool) =>
        frame
            .Adjustments.Where(adjustment =>
                adjustment.DamageType == type && adjustment.Pool == pool && adjustment.Amount > 0
            )
            .Sum(adjustment => (long)adjustment.Amount);
}

internal sealed record CandidateCoverage(
    int Observed,
    int Matched,
    int Mismatched,
    int Skipped,
    long MatchedAmount,
    long MismatchedAmount
);

internal sealed class CandidateCounter
{
    private int _observed;
    private int _matched;
    private int _mismatched;
    private int _skipped;
    private long _matchedAmount;
    private long _mismatchedAmount;

    internal void Observe(bool matched, long amount)
    {
        _observed++;
        if (matched)
        {
            _matched++;
            _matchedAmount += amount;
        }
        else
        {
            _mismatched++;
            _mismatchedAmount += amount;
        }
    }

    internal void Skip() => _skipped++;

    internal CandidateCoverage Build() =>
        new(_observed, _matched, _mismatched, _skipped, _matchedAmount, _mismatchedAmount);
}

internal sealed record PoolLedgerCoverage(
    int Observed,
    int Reconciled,
    int Mismatched,
    int Skipped,
    long AttemptedGain,
    long RealizedGain,
    long Loss
);

internal sealed class PoolLedgerCounter
{
    private int _observed;
    private int _reconciled;
    private int _mismatched;
    private int _skipped;
    private long _attemptedGain;
    private long _realizedGain;
    private long _loss;

    internal void Observe(bool reconciled, long attemptedGain, long realizedGain, long loss)
    {
        _observed++;
        if (reconciled)
            _reconciled++;
        else
            _mismatched++;
        _attemptedGain += attemptedGain;
        _realizedGain += realizedGain;
        _loss += loss;
    }

    internal void Skip() => _skipped++;

    internal PoolLedgerCoverage Build() =>
        new(_observed, _reconciled, _mismatched, _skipped, _attemptedGain, _realizedGain, _loss);
}

internal sealed class ApplicationCoverage
{
    public int GainFrames { get; private set; }
    public int SingleResolvedSource { get; private set; }
    public int MultipleResolvedSources { get; private set; }
    public int NoResolvedSource { get; private set; }
    public int ApplyWithoutVisibleGain { get; private set; }
    public int UnexpectedLossFrames { get; private set; }

    internal void Observe(PeriodicFrameObservation frame)
    {
        ObserveStatus(frame, "Burn", frame.Before.Burn, frame.After.Burn);
        ObserveStatus(frame, "Poison", frame.Before.Poison, frame.After.Poison);
        ObserveStatus(frame, "Regen", frame.Before.Regen, frame.After.Regen);
    }

    private void ObserveStatus(
        PeriodicFrameObservation frame,
        string status,
        int? before,
        int? after
    )
    {
        if (!before.HasValue || !after.HasValue)
            return;

        var actionName = $"Player{status}Apply";
        var sources = frame
            .Actions.Where(action => action.Action == actionName)
            .Select(action => action.ResolvedSourceId)
            .Where(source => source != null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var hasApply = frame.Actions.Any(action => action.Action == actionName);
        var baseline = before.Value;
        if (
            status == "Burn"
            && (Loss(frame, "Burn", "Health") > 0 || Loss(frame, "Burn", "Shield") > 0)
            && before.Value > 0
        )
        {
            baseline -= Math.Max(1, (int)Math.Floor(before.Value * 0.03));
        }
        var gain = after.Value - baseline;
        if (gain > 0)
        {
            GainFrames++;
            if (sources.Length == 1)
                SingleResolvedSource++;
            else if (sources.Length > 1)
                MultipleResolvedSources++;
            else
                NoResolvedSource++;
            return;
        }

        if (hasApply)
            ApplyWithoutVisibleGain++;
        if (gain < 0)
            UnexpectedLossFrames++;
    }

    private static long Loss(PeriodicFrameObservation frame, string type, string pool) =>
        -frame
            .Adjustments.Where(adjustment =>
                adjustment.DamageType == type && adjustment.Pool == pool && adjustment.Amount < 0
            )
            .Sum(adjustment => (long)adjustment.Amount);
}

internal sealed class AttributionCoverage
{
    public string ModelVersion { get; } = PeriodicEffectAttribution.ModelVersion;
    public int BattlesWithResults { get; private set; }
    public int Results { get; private set; }
    public int ExactResults { get; private set; }
    public int ProportionalResults { get; private set; }
    public SortedDictionary<string, int> ResultsByKindAndProof { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> HealthByKind { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> ShieldByKind { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> HealthByKindAndProof { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> ShieldByKindAndProof { get; } =
        new(StringComparer.Ordinal);
    public int AllocationDecisions { get; private set; }
    public int ExactAllocationDecisions { get; private set; }
    public int ProportionalAllocationDecisions { get; private set; }
    public SortedDictionary<string, int> AllocationDecisionsByKindAndProof { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> AllocatedHealthByKindAndProof { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> AllocatedShieldByKindAndProof { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> MeasuredHealthByCombatantAndKind { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> MeasuredShieldByCombatantAndKind { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> AllocatedHealthByCombatantAndKind { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> AllocatedShieldByCombatantAndKind { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> ResidualHealthByCombatantAndKind { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> ResidualShieldByCombatantAndKind { get; } =
        new(StringComparer.Ordinal);
    public int GapFrames { get; private set; }
    public int UnknownFrames { get; private set; }
    public SortedDictionary<string, int> UnknownFramesByKind { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> UnattributedHealthByKind { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> UnattributedShieldByKind { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> UnattributedHealthByOrigin { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> UnattributedShieldByOrigin { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> UnattributedHealthByContext { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> UnattributedRegenByCandidateCount { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> UnattributedRegenByCandidateSurfaces { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> RegenRealizedBySpawnBaseline { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> RegenAttributedBySpawnBaseline { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> RegenAttributedBySourceSurfaces { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> RegenAttributedSpawnBaselineSources { get; } =
        new(StringComparer.Ordinal);
    public SortedDictionary<string, long> RegenUnknownBySpawnBaseline { get; } =
        new(StringComparer.Ordinal);
    public long ExactHealthAmount { get; private set; }
    public long ProportionalHealthAmount { get; private set; }
    public long ExactShieldAmount { get; private set; }
    public long ProportionalShieldAmount { get; private set; }
    public long ExcludedStatusAbsentRegenHealth { get; private set; }
    public int RegenFramesWithAttempt { get; private set; }
    public int RegenDeadFrames { get; private set; }
    public int RegenDeathEventFrames { get; private set; }
    public int RegenDeadFlagWithoutEventFrames { get; private set; }
    public int RegenDeathEventWithoutFlagFrames { get; private set; }
    public long RegenAttemptedOnDeadFrames { get; private set; }
    public long RegenPoolAcceptedOnDeadFrames { get; private set; }
    public long RegenAttemptedOnDeathEventFrames { get; private set; }
    public long RegenPoolAcceptedOnDeathEventFrames { get; private set; }
    public long RegenRealizedOnDeathEventFrames { get; private set; }
    public long RegenPoolAcceptedAfterNonPositiveHealthOnDeathEventFrames { get; private set; }
    public long RegenPoolAcceptedAfterNonPositiveHealth { get; private set; }
    public long RegenAttributedOnDeadFrames { get; private set; }
    public long RegenUnknownOnDeadFrames { get; private set; }
    public long RegenAttributedOnDeathEventFrames { get; private set; }
    public long RegenUnknownOnDeathEventFrames { get; private set; }
    public long RegenStableSpawnBaselineRealized { get; private set; }
    public long RegenStableSpawnBaselineAttributed { get; private set; }
    public long RegenStableSpawnBaselineUnknown { get; private set; }
    public int RegenGapMeasurementMismatchFrames { get; private set; }
    public long RegenGapMeasurementSignedDifference { get; private set; }
    public long RegenGapMeasurementAbsoluteDifference { get; private set; }

    internal AttributionCoverage()
    {
        foreach (var kind in Enum.GetNames<CombatImpactPeriodicKind>())
        {
            UnknownFramesByKind[kind] = 0;
            foreach (var proof in Enum.GetNames<CombatImpactPeriodicProof>())
            {
                var key = $"{kind}/{proof}";
                ResultsByKindAndProof[key] = 0;
                HealthByKindAndProof[key] = 0;
                ShieldByKindAndProof[key] = 0;
                AllocationDecisionsByKindAndProof[key] = 0;
                AllocatedHealthByKindAndProof[key] = 0;
                AllocatedShieldByKindAndProof[key] = 0;
            }
        }
    }

    internal void Observe(
        PeriodicAttributionReport report,
        CombatSim combat,
        IReadOnlyDictionary<string, CardIdentity> identities,
        GameSim spawn
    )
    {
        var impacts = report.SourceImpacts;
        ValidateLedger(report);
        foreach (var (key, measured) in report.MeasuredTotals)
        {
            var ledgerKey = $"{key.Combatant}/{key.Kind}";
            MeasuredHealthByCombatantAndKind[ledgerKey] =
                MeasuredHealthByCombatantAndKind.GetValueOrDefault(ledgerKey)
                + measured.HealthAmount;
            MeasuredShieldByCombatantAndKind[ledgerKey] =
                MeasuredShieldByCombatantAndKind.GetValueOrDefault(ledgerKey)
                + measured.ShieldAmount;
        }
        var regenMeasurements = BuildRegenMeasurements(combat, spawn);
        foreach (var pair in regenMeasurements)
        {
            var measurement = pair.Value;
            RegenFramesWithAttempt++;
            var baselineKey = DescribeRegenSpawnBaseline(pair.Key.Combatant, measurement);
            RegenRealizedBySpawnBaseline[baselineKey] =
                RegenRealizedBySpawnBaseline.GetValueOrDefault(baselineKey) + measurement.Realized;
            if (IsUnobservableSpawnBaseline(measurement))
                RegenStableSpawnBaselineRealized += measurement.Realized;
            if (measurement.IsPlayerDead)
            {
                RegenDeadFrames++;
                RegenAttemptedOnDeadFrames += measurement.Attempted;
                RegenPoolAcceptedOnDeadFrames += measurement.PoolAccepted;
            }
            if (measurement.HasDeathEvent)
            {
                RegenDeathEventFrames++;
                RegenAttemptedOnDeathEventFrames += measurement.Attempted;
                RegenPoolAcceptedOnDeathEventFrames += measurement.PoolAccepted;
                RegenRealizedOnDeathEventFrames += measurement.Realized;
                RegenPoolAcceptedAfterNonPositiveHealthOnDeathEventFrames +=
                    measurement.PoolAcceptedAfterNonPositiveHealth;
            }
            if (measurement.IsPlayerDead && !measurement.HasDeathEvent)
                RegenDeadFlagWithoutEventFrames++;
            if (!measurement.IsPlayerDead && measurement.HasDeathEvent)
                RegenDeathEventWithoutFlagFrames++;
            RegenPoolAcceptedAfterNonPositiveHealth +=
                measurement.PoolAcceptedAfterNonPositiveHealth;
        }
        if (impacts.Count > 0)
            BattlesWithResults++;
        foreach (
            var (key, impact) in impacts
                .OrderBy(pair => pair.Key.SourceId, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Combatant)
                .ThenBy(pair => pair.Key.Kind)
        )
        {
            Results++;
            var kind = key.Kind.ToString();
            var proof = impact.Proof.ToString();
            var proofKey = $"{kind}/{proof}";
            ResultsByKindAndProof[proofKey]++;
            HealthByKind[kind] = HealthByKind.GetValueOrDefault(kind) + impact.HealthAmount;
            ShieldByKind[kind] = ShieldByKind.GetValueOrDefault(kind) + impact.ShieldAmount;
            HealthByKindAndProof[proofKey] += impact.HealthAmount;
            ShieldByKindAndProof[proofKey] += impact.ShieldAmount;
            switch (impact.Proof)
            {
                case CombatImpactPeriodicProof.Exact:
                    ExactResults++;
                    ExactHealthAmount += impact.HealthAmount;
                    ExactShieldAmount += impact.ShieldAmount;
                    break;
                case CombatImpactPeriodicProof.Proportional:
                    ProportionalResults++;
                    ProportionalHealthAmount += impact.HealthAmount;
                    ProportionalShieldAmount += impact.ShieldAmount;
                    break;
            }
        }

        foreach (var allocation in report.Allocations)
        {
            AllocationDecisions++;
            var allocationKey = $"{allocation.Kind}/{allocation.Proof}";
            AllocationDecisionsByKindAndProof[allocationKey]++;
            AllocatedHealthByKindAndProof[allocationKey] += allocation.HealthAmount;
            AllocatedShieldByKindAndProof[allocationKey] += allocation.ShieldAmount;
            var ledgerKey = $"{allocation.Combatant}/{allocation.Kind}";
            AllocatedHealthByCombatantAndKind[ledgerKey] =
                AllocatedHealthByCombatantAndKind.GetValueOrDefault(ledgerKey)
                + allocation.HealthAmount;
            AllocatedShieldByCombatantAndKind[ledgerKey] =
                AllocatedShieldByCombatantAndKind.GetValueOrDefault(ledgerKey)
                + allocation.ShieldAmount;
            if (
                allocation.Kind == CombatImpactPeriodicKind.Regen
                && regenMeasurements.TryGetValue(
                    (allocation.FrameIndex, allocation.Combatant),
                    out var allocationMeasurement
                )
            )
            {
                var baselineKey = DescribeRegenSpawnBaseline(
                    allocation.Combatant,
                    allocationMeasurement
                );
                RegenAttributedBySpawnBaseline[baselineKey] =
                    RegenAttributedBySpawnBaseline.GetValueOrDefault(baselineKey)
                    + allocation.HealthAmount;
                var surfaceKey = DescribeRegenAllocationSurface(
                    allocation,
                    combat,
                    identities,
                    spawn
                );
                RegenAttributedBySourceSurfaces[surfaceKey] =
                    RegenAttributedBySourceSurfaces.GetValueOrDefault(surfaceKey)
                    + allocation.HealthAmount;
                if (IsUnobservableSpawnBaseline(allocationMeasurement))
                {
                    RegenStableSpawnBaselineAttributed += allocation.HealthAmount;
                    var sourceKey = DescribeRegenAllocationSource(
                        allocation,
                        combat,
                        identities,
                        spawn
                    );
                    RegenAttributedSpawnBaselineSources[sourceKey] =
                        RegenAttributedSpawnBaselineSources.GetValueOrDefault(sourceKey)
                        + allocation.HealthAmount;
                }
                if (allocationMeasurement.IsPlayerDead)
                    RegenAttributedOnDeadFrames += allocation.HealthAmount;
            }
            if (
                allocation.Kind == CombatImpactPeriodicKind.Regen
                && regenMeasurements.TryGetValue(
                    (allocation.FrameIndex, allocation.Combatant),
                    out var deathEventAllocationMeasurement
                )
                && deathEventAllocationMeasurement.HasDeathEvent
            )
            {
                RegenAttributedOnDeathEventFrames += allocation.HealthAmount;
            }
            switch (allocation.Proof)
            {
                case CombatImpactPeriodicProof.Exact:
                    ExactAllocationDecisions++;
                    break;
                case CombatImpactPeriodicProof.Proportional:
                    ProportionalAllocationDecisions++;
                    break;
            }
        }

        foreach (var gap in report.Residuals)
        {
            GapFrames++;
            var kind = gap.Kind.ToString();
            var origin = gap.Origin.ToString();
            var ledgerKey = $"{gap.Combatant}/{gap.Kind}";
            ResidualHealthByCombatantAndKind[ledgerKey] =
                ResidualHealthByCombatantAndKind.GetValueOrDefault(ledgerKey) + gap.HealthAmount;
            ResidualShieldByCombatantAndKind[ledgerKey] =
                ResidualShieldByCombatantAndKind.GetValueOrDefault(ledgerKey) + gap.ShieldAmount;
            var isMeasuredSourceImpact = gap.Kind != CombatImpactPeriodicKind.Regen;
            var bookedGapHealth = gap.HealthAmount;
            if (
                gap.Kind == CombatImpactPeriodicKind.Regen
                && regenMeasurements.TryGetValue(
                    (gap.FrameIndex, gap.Combatant),
                    out var measuredGap
                )
                && gap.HealthAmount != measuredGap.Realized
            )
            {
                var difference = gap.HealthAmount - measuredGap.Realized;
                RegenGapMeasurementMismatchFrames++;
                RegenGapMeasurementSignedDifference += difference;
                RegenGapMeasurementAbsoluteDifference += Math.Abs(difference);
            }
            if (
                gap.Kind == CombatImpactPeriodicKind.Regen
                && regenMeasurements.TryGetValue(
                    (gap.FrameIndex, gap.Combatant),
                    out var regenMeasurement
                )
                && regenMeasurement.HasSourceStatus
                && regenMeasurement.HealthReconciled
            )
            {
                isMeasuredSourceImpact = true;
                if (
                    gap.Origin.HasFlag(PeriodicUnknownOrigin.HealthLedgerUnreconciled)
                    || gap.Origin.HasFlag(PeriodicUnknownOrigin.StatusStateAbsent)
                )
                {
                    bookedGapHealth = regenMeasurement.Realized;
                }
            }
            if (isMeasuredSourceImpact)
            {
                if (
                    gap.Kind == CombatImpactPeriodicKind.Regen
                    && regenMeasurements.TryGetValue(
                        (gap.FrameIndex, gap.Combatant),
                        out var gapMeasurement
                    )
                    && gapMeasurement.IsPlayerDead
                )
                {
                    RegenUnknownOnDeadFrames += bookedGapHealth;
                }
                if (
                    gap.Kind == CombatImpactPeriodicKind.Regen
                    && regenMeasurements.TryGetValue(
                        (gap.FrameIndex, gap.Combatant),
                        out var deathEventGapMeasurement
                    )
                    && deathEventGapMeasurement.HasDeathEvent
                )
                {
                    RegenUnknownOnDeathEventFrames += bookedGapHealth;
                }
                if (bookedGapHealth > 0 || gap.ShieldAmount > 0)
                {
                    UnknownFrames++;
                    UnknownFramesByKind[kind]++;
                }
                UnattributedHealthByKind[kind] =
                    UnattributedHealthByKind.GetValueOrDefault(kind) + bookedGapHealth;
                UnattributedShieldByKind[kind] =
                    UnattributedShieldByKind.GetValueOrDefault(kind) + gap.ShieldAmount;
                if (
                    gap.Kind == CombatImpactPeriodicKind.Regen
                    && regenMeasurements.TryGetValue(
                        (gap.FrameIndex, gap.Combatant),
                        out var unknownMeasurement
                    )
                )
                {
                    var baselineKey =
                        $"{origin}/"
                        + DescribeRegenSpawnBaseline(gap.Combatant, unknownMeasurement);
                    RegenUnknownBySpawnBaseline[baselineKey] =
                        RegenUnknownBySpawnBaseline.GetValueOrDefault(baselineKey)
                        + bookedGapHealth;
                    if (IsUnobservableSpawnBaseline(unknownMeasurement))
                        RegenStableSpawnBaselineUnknown += bookedGapHealth;
                }
            }
            else
            {
                ExcludedStatusAbsentRegenHealth += gap.HealthAmount;
            }
            UnattributedHealthByOrigin[origin] =
                UnattributedHealthByOrigin.GetValueOrDefault(origin) + gap.HealthAmount;
            UnattributedShieldByOrigin[origin] =
                UnattributedShieldByOrigin.GetValueOrDefault(origin) + gap.ShieldAmount;
            var frame = combat.Frames[gap.FrameIndex];
            var hasSameFrameApply = frame
                .Events.OfType<CombatSimEventEffectExecuted>()
                .Any(effect =>
                    effect.ActionType == EActionCommandType.PlayerRegenApply
                    && effect.Target is EffectTargetPlayer player
                    && player.Target == gap.Combatant
                );
            var context =
                $"{origin}/{(isMeasuredSourceImpact ? "MeasuredSource" : "Excluded")}/"
                + $"{(hasSameFrameApply ? "ApplySameFrame" : "NoApply")}/"
                + $"{(gap.FrameIndex == 0 ? "Frame0" : "Later")}";
            UnattributedHealthByContext[context] =
                UnattributedHealthByContext.GetValueOrDefault(context) + gap.HealthAmount;
            if (gap.Kind == CombatImpactPeriodicKind.Regen && gap.HealthAmount > 0)
            {
                var candidateCount = combat.CardStats.Count(pair =>
                    pair.Value.GetValueOrDefault(ECardStats.RegenAdded) > 0
                    && identities.TryGetValue(pair.Key, out var identity)
                    && identity.Combatant == gap.Combatant
                    && identity.Type is ECardType.Item or ECardType.Skill
                );
                var candidateKey =
                    $"{origin}/{(isMeasuredSourceImpact ? "MeasuredSource" : "Excluded")}/"
                    + candidateCount;
                UnattributedRegenByCandidateCount[candidateKey] =
                    UnattributedRegenByCandidateCount.GetValueOrDefault(candidateKey)
                    + gap.HealthAmount;
                var attributeCandidateCount = spawn.Cards.Count(pair =>
                    pair.Value.Attributes.TryGetValue(
                        ECardAttributeType.RegenApplyAmount,
                        out var attribute
                    )
                    && attribute.Value > 0
                    && identities.TryGetValue(pair.Key, out var identity)
                    && identity.Combatant == gap.Combatant
                    && identity.Type is ECardType.Item or ECardType.Skill
                );
                var eventCandidateIds = combat
                    .Frames.SelectMany(frame => frame.Events.OfType<CombatSimEventEffectExecuted>())
                    .Where(effect =>
                        effect.ActionType == EActionCommandType.PlayerRegenApply
                        && effect.Target is EffectTargetPlayer player
                        && player.Target == gap.Combatant
                    )
                    .Select(effect => CorpusInventory.ResolveSource(effect, identities))
                    .Where(sourceId => sourceId != null)
                    .Select(sourceId => sourceId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var eventCandidateCount = eventCandidateIds.Length;
                var priorEventCandidateCount = combat
                    .Frames.Take(gap.FrameIndex + 1)
                    .SelectMany(frame => frame.Events.OfType<CombatSimEventEffectExecuted>())
                    .Where(effect =>
                        effect.ActionType == EActionCommandType.PlayerRegenApply
                        && effect.Target is EffectTargetPlayer player
                        && player.Target == gap.Combatant
                    )
                    .Select(effect => CorpusInventory.ResolveSource(effect, identities))
                    .Where(sourceId => sourceId != null)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                var displayCandidateCount = combat
                    .CardStats.Where(pair =>
                        pair.Value.GetValueOrDefault(ECardStats.RegenAdded) > 0
                        && identities.TryGetValue(pair.Key, out var identity)
                        && identity.Combatant == gap.Combatant
                        && identity.Type is ECardType.Item or ECardType.Skill
                    )
                    .Select(pair => pair.Key)
                    .Concat(eventCandidateIds)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                var surfaceKey =
                    $"{origin}/{(isMeasuredSourceImpact ? "MeasuredSource" : "Excluded")}/"
                    + $"stats:{candidateCount}/attributes:{attributeCandidateCount}/"
                    + $"events:{eventCandidateCount}/prior:{priorEventCandidateCount}/"
                    + $"display:{displayCandidateCount}";
                UnattributedRegenByCandidateSurfaces[surfaceKey] =
                    UnattributedRegenByCandidateSurfaces.GetValueOrDefault(surfaceKey)
                    + gap.HealthAmount;
            }
        }
    }

    private static void ValidateLedger(PeriodicAttributionReport report)
    {
        var booked = new Dictionary<PeriodicMeasurementKey, PeriodicMeasuredAmounts>();
        foreach (var allocation in report.Allocations)
        {
            AddBooked(
                booked,
                new PeriodicMeasurementKey(allocation.Combatant, allocation.Kind),
                allocation.HealthAmount,
                allocation.ShieldAmount
            );
        }
        foreach (var residual in report.Residuals)
        {
            AddBooked(
                booked,
                new PeriodicMeasurementKey(residual.Combatant, residual.Kind),
                residual.HealthAmount,
                residual.ShieldAmount
            );
        }

        foreach (var key in report.MeasuredTotals.Keys.Union(booked.Keys))
        {
            report.MeasuredTotals.TryGetValue(key, out var measured);
            booked.TryGetValue(key, out var accounted);
            if (measured != accounted)
            {
                throw new InvalidOperationException(
                    $"Periodic attribution ledger did not reconcile for {key.Combatant}/{key.Kind}: "
                        + $"measured health={measured.HealthAmount}, shield={measured.ShieldAmount}; "
                        + $"accounted health={accounted.HealthAmount}, shield={accounted.ShieldAmount}."
                );
            }
        }

        var sourceRollups = report.Allocations.GroupBy(allocation => new PeriodicImpactKey(
            allocation.SourceId,
            allocation.Combatant,
            allocation.Kind
        ));
        foreach (var rollup in sourceRollups)
        {
            if (!report.SourceImpacts.TryGetValue(rollup.Key, out var impact))
                throw new InvalidOperationException(
                    $"Missing periodic source rollup for {rollup.Key}."
                );
            var health = rollup.Sum(allocation => allocation.HealthAmount);
            var shield = rollup.Sum(allocation => allocation.ShieldAmount);
            var proof = rollup.Any(allocation =>
                allocation.Proof == CombatImpactPeriodicProof.Proportional
            )
                ? CombatImpactPeriodicProof.Proportional
                : CombatImpactPeriodicProof.Exact;
            if (
                impact.HealthAmount != health
                || impact.ShieldAmount != shield
                || impact.Proof != proof
            )
            {
                throw new InvalidOperationException(
                    $"Periodic source rollup did not reconcile for {rollup.Key}."
                );
            }
        }
        if (report.SourceImpacts.Count != sourceRollups.Count())
            throw new InvalidOperationException(
                "Periodic source rollup contains unbooked entries."
            );
    }

    private static void AddBooked(
        IDictionary<PeriodicMeasurementKey, PeriodicMeasuredAmounts> totals,
        PeriodicMeasurementKey key,
        long health,
        long shield
    )
    {
        totals.TryGetValue(key, out var current);
        totals[key] = new PeriodicMeasuredAmounts(
            current.HealthAmount + health,
            current.ShieldAmount + shield
        );
    }

    private static Dictionary<
        (int FrameIndex, ECombatantId Combatant),
        RegenFrameMeasurement
    > BuildRegenMeasurements(CombatSim combat, GameSim spawn)
    {
        var result = new Dictionary<(int, ECombatantId), RegenFrameMeasurement>();
        var states = new Dictionary<ECombatantId, Dictionary<EPlayerAttributeType, int>>
        {
            [ECombatantId.Player] = new Dictionary<EPlayerAttributeType, int>(
                spawn.Player.Attributes
            ),
            [ECombatantId.Opponent] = new Dictionary<EPlayerAttributeType, int>(
                spawn.Opponent.Attributes
            ),
        };
        var spawnRegen = new Dictionary<ECombatantId, int>
        {
            [ECombatantId.Player] = spawn.Player.Attributes.GetValueOrDefault(
                EPlayerAttributeType.HealthRegen
            ),
            [ECombatantId.Opponent] = spawn.Opponent.Attributes.GetValueOrDefault(
                EPlayerAttributeType.HealthRegen
            ),
        };
        var hasAnyRegenTransition = new Dictionary<ECombatantId, bool>
        {
            [ECombatantId.Player] = false,
            [ECombatantId.Opponent] = false,
        };
        foreach (var combatant in new[] { ECombatantId.Player, ECombatantId.Opponent })
        {
            var state = states[combatant];
            foreach (var frame in combat.Frames)
            {
                var update =
                    combatant == ECombatantId.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
                if (update == null)
                    continue;
                if (update.Attributes.ContainsKey(EPlayerAttributeType.HealthRegen))
                    hasAnyRegenTransition[combatant] = true;
                foreach (var (attribute, transition) in update.Attributes)
                    state.TryAdd(attribute, transition.PreviousValue);
            }
            if (
                !state.ContainsKey(EPlayerAttributeType.HealthMax)
                && state.TryGetValue(EPlayerAttributeType.Health, out var health)
            )
            {
                state[EPlayerAttributeType.HealthMax] = health;
            }
        }
        for (var frameIndex = 0; frameIndex < combat.Frames.Count; frameIndex++)
        {
            var frame = combat.Frames[frameIndex];
            foreach (var combatant in new[] { ECombatantId.Player, ECombatantId.Opponent })
            {
                var state = states[combatant];
                var update =
                    combatant == ECombatantId.Player ? frame.PlayerUpdates : frame.OpponentUpdates;
                if (update == null)
                    continue;

                var openingRegen = state.GetValueOrDefault(EPlayerAttributeType.HealthRegen);
                update.Attributes.TryGetValue(
                    EPlayerAttributeType.HealthRegen,
                    out var regenTransition
                );
                var hasFrameRegenTransition = regenTransition != null;
                var closingRegen = regenTransition?.CurrentValue ?? openingRegen;
                var attempted = update
                    .HealthAdjustments.Where(adjustment =>
                        adjustment.DamageType == EDamageType.Regen
                        && adjustment.AttributeChanged == EPlayerHealthChangeType.Health
                        && adjustment.Amount > 0
                    )
                    .Sum(adjustment => (long)adjustment.Amount);
                if (attempted > 0)
                {
                    var hasDeathEvent = frame
                        .Events.OfType<CombatSimEventCombatantDied>()
                        .Any(death => death.CombatantId == combatant);
                    var reconciled = TryMeasureAcceptedRegen(
                        state,
                        update,
                        hasDeathEvent,
                        out var poolAccepted,
                        out var realized,
                        out var poolAcceptedAfterNonPositiveHealth
                    );
                    result[(frameIndex, combatant)] = new RegenFrameMeasurement(
                        attempted,
                        poolAccepted,
                        realized,
                        poolAcceptedAfterNonPositiveHealth,
                        reconciled,
                        openingRegen > 0 || closingRegen > 0,
                        update.IsPlayerDead,
                        hasDeathEvent,
                        spawnRegen[combatant],
                        openingRegen,
                        closingRegen,
                        hasFrameRegenTransition,
                        hasAnyRegenTransition[combatant]
                    );
                }

                foreach (var (attribute, transition) in update.Attributes)
                    state[attribute] = transition.CurrentValue;
            }
        }
        return result;
    }

    private static string DescribeRegenSpawnBaseline(
        ECombatantId combatant,
        RegenFrameMeasurement measurement
    )
    {
        var attemptRelation =
            measurement.OpeningRegen <= 0 ? "attempt:opening-zero"
            : measurement.Attempted == measurement.OpeningRegen ? "attempt:equals-opening"
            : measurement.Attempted % measurement.OpeningRegen == 0 ? "attempt:multiple-opening"
            : "attempt:other";
        return $"{combatant}/"
            + $"spawn:{(measurement.SpawnRegen > 0 ? "positive" : "zero")}/"
            + $"combat-transition:{(measurement.HasAnyRegenTransition ? "yes" : "no")}/"
            + $"frame-transition:{(measurement.HasFrameRegenTransition ? "yes" : "no")}/"
            + attemptRelation;
    }

    private static bool IsUnobservableSpawnBaseline(RegenFrameMeasurement measurement) =>
        measurement.HealthReconciled
        && measurement.SpawnRegen > 0
        && !measurement.HasAnyRegenTransition
        && measurement.OpeningRegen > 0
        && measurement.Attempted == measurement.OpeningRegen;

    private static string DescribeRegenAllocationSource(
        PeriodicAttributionAllocation allocation,
        CombatSim combat,
        IReadOnlyDictionary<string, CardIdentity> identities,
        GameSim spawn
    )
    {
        identities.TryGetValue(allocation.SourceId, out var identity);
        return $"{identity?.TemplateId ?? allocation.SourceId}/"
            + DescribeRegenAllocationSurface(allocation, combat, identities, spawn);
    }

    private static string DescribeRegenAllocationSurface(
        PeriodicAttributionAllocation allocation,
        CombatSim combat,
        IReadOnlyDictionary<string, CardIdentity> identities,
        GameSim spawn
    )
    {
        var hasStats =
            combat.CardStats.TryGetValue(allocation.SourceId, out var stats)
            && stats.GetValueOrDefault(ECardStats.RegenAdded) > 0;
        var hasAttribute =
            spawn.Cards.TryGetValue(allocation.SourceId, out var card)
            && card.Attributes.TryGetValue(
                ECardAttributeType.RegenApplyAmount,
                out var regenApplyAmount
            )
            && regenApplyAmount.Value > 0;
        var eventFrames = combat
            .Frames.Select((frame, index) => (frame, index))
            .Where(pair => pair.index <= allocation.FrameIndex)
            .SelectMany(pair => pair.frame.Events.OfType<CombatSimEventEffectExecuted>())
            .Where(effect =>
                effect.ActionType == EActionCommandType.PlayerRegenApply
                && effect.Target is EffectTargetPlayer player
                && player.Target == allocation.Combatant
                && CorpusInventory.ResolveSource(effect, identities) == allocation.SourceId
            )
            .Count();
        return $"stats:{(hasStats ? "yes" : "no")}/"
            + $"attribute:{(hasAttribute ? "yes" : "no")}/"
            + $"prior-events:{(eventFrames > 0 ? "yes" : "no")}";
    }

    private static bool TryMeasureAcceptedRegen(
        IReadOnlyDictionary<EPlayerAttributeType, int> state,
        CombatSimPlayerUpdate update,
        bool combatantDied,
        out long poolAccepted,
        out long realized,
        out long poolAcceptedAfterNonPositiveHealth
    )
    {
        poolAccepted = 0;
        realized = 0;
        poolAcceptedAfterNonPositiveHealth = 0;
        if (
            !state.TryGetValue(EPlayerAttributeType.Health, out var openingHealth)
            || !state.TryGetValue(EPlayerAttributeType.HealthMax, out var openingMax)
        )
        {
            return false;
        }

        var closingMax = update.Attributes.TryGetValue(
            EPlayerAttributeType.HealthMax,
            out var maxTransition
        )
            ? maxTransition.CurrentValue
            : openingMax;
        long health = openingHealth + (closingMax - openingMax);
        var terminalReached = combatantDied && health <= 0;
        foreach (
            var adjustment in update.HealthAdjustments.Where(adjustment =>
                adjustment.AttributeChanged == EPlayerHealthChangeType.Health
            )
        )
        {
            if (adjustment.Amount <= 0)
            {
                health += adjustment.Amount;
                if (combatantDied && health <= 0)
                    terminalReached = true;
                continue;
            }
            var accepted = Math.Min(adjustment.Amount, Math.Max(0L, closingMax - health));
            health += accepted;
            if (adjustment.DamageType == EDamageType.Regen)
            {
                poolAccepted += accepted;
                if (terminalReached)
                    poolAcceptedAfterNonPositiveHealth += accepted;
                if (!combatantDied || !terminalReached)
                    realized += accepted;
            }
        }

        var closingHealth = update.Attributes.TryGetValue(
            EPlayerAttributeType.Health,
            out var healthTransition
        )
            ? healthTransition.CurrentValue
            : health;
        return health == closingHealth;
    }

    private readonly record struct RegenFrameMeasurement(
        long Attempted,
        long PoolAccepted,
        long Realized,
        long PoolAcceptedAfterNonPositiveHealth,
        bool HealthReconciled,
        bool HasSourceStatus,
        bool IsPlayerDead,
        bool HasDeathEvent,
        int SpawnRegen,
        int OpeningRegen,
        int ClosingRegen,
        bool HasFrameRegenTransition,
        bool HasAnyRegenTransition
    );
}

internal sealed class RealizedImpactTotals
{
    public long BurnHealthDamage { get; private set; }
    public long BurnShieldConsumed { get; private set; }
    public long PoisonHealthDamage { get; private set; }
    public long RegenAttempted { get; private set; }
    public long RegenRealized { get; private set; }

    internal void Observe(PeriodicFrameObservation frame)
    {
        BurnHealthDamage += EffectiveHealthLoss(frame, "Burn");
        BurnShieldConsumed += EffectiveBurnShieldLoss(frame);
        PoisonHealthDamage += EffectiveHealthLoss(frame, "Poison");

        var attempted = Gain(frame, "Regen", "Health");
        if (
            attempted <= 0
            || ((frame.Before.Regen ?? 0) <= 0 && (frame.After.Regen ?? 0) <= 0)
            || !frame.Before.Health.HasValue
            || !frame.After.Health.HasValue
            || !frame.Before.HealthMax.HasValue
            || !frame.After.HealthMax.HasValue
        )
        {
            return;
        }

        long health =
            frame.Before.Health.Value
            + (frame.After.HealthMax.Value - frame.Before.HealthMax.Value);
        var terminalReached = frame.HasDeathEvent && health <= 0;
        long realized = 0;
        foreach (
            var adjustment in frame.Adjustments.Where(adjustment => adjustment.Pool == "Health")
        )
        {
            if (adjustment.Amount <= 0)
            {
                health += adjustment.Amount;
                if (frame.HasDeathEvent && health <= 0)
                    terminalReached = true;
                continue;
            }

            var accepted = Math.Min(
                adjustment.Amount,
                Math.Max(0, frame.After.HealthMax.Value - health)
            );
            health += accepted;
            if (adjustment.DamageType == "Regen" && (!frame.HasDeathEvent || !terminalReached))
            {
                realized += accepted;
            }
        }
        if (health != frame.After.Health.Value)
            return;

        RegenAttempted += attempted;
        RegenRealized += realized;
    }

    private static long Loss(PeriodicFrameObservation frame, string type, string pool) =>
        -frame
            .Adjustments.Where(adjustment =>
                adjustment.DamageType == type && adjustment.Pool == pool && adjustment.Amount < 0
            )
            .Sum(adjustment => (long)adjustment.Amount);

    private static long EffectiveHealthLoss(PeriodicFrameObservation frame, string type)
    {
        var raw = Loss(frame, type, "Health");
        if (
            !frame.HasDeathEvent
            || !frame.Before.Health.HasValue
            || !frame.Before.HealthMax.HasValue
            || !frame.After.HealthMax.HasValue
        )
        {
            return raw;
        }

        long health =
            frame.Before.Health.Value
            + (frame.After.HealthMax.Value - frame.Before.HealthMax.Value);
        var terminalReached = health <= 0;
        var effective = 0L;
        foreach (
            var adjustment in frame.Adjustments.Where(adjustment => adjustment.Pool == "Health")
        )
        {
            if (adjustment.Amount <= 0)
            {
                var loss = -(long)adjustment.Amount;
                if (adjustment.DamageType == type)
                    effective += terminalReached ? 0L : Math.Min(loss, Math.Max(0L, health));
                health += adjustment.Amount;
                if (health <= 0)
                    terminalReached = true;
                continue;
            }

            var accepted = Math.Min(
                adjustment.Amount,
                Math.Max(0L, frame.After.HealthMax.Value - health)
            );
            health += accepted;
        }
        return effective;
    }

    private static long EffectiveBurnShieldLoss(PeriodicFrameObservation frame)
    {
        var raw = Loss(frame, "Burn", "Shield");
        if (
            !frame.HasDeathEvent
            || !frame.Before.Health.HasValue
            || !frame.Before.HealthMax.HasValue
            || !frame.After.HealthMax.HasValue
        )
        {
            return raw;
        }

        long health =
            frame.Before.Health.Value
            + (frame.After.HealthMax.Value - frame.Before.HealthMax.Value);
        var terminalReached = health <= 0;
        var effective = 0L;
        for (var index = 0; index < frame.Adjustments.Count; index++)
        {
            var adjustment = frame.Adjustments[index];
            if (
                adjustment.DamageType == "Burn"
                && adjustment.Amount < 0
                && adjustment.Pool is "Health" or "Shield"
            )
            {
                var groupWasAlive = !terminalReached;
                while (
                    index < frame.Adjustments.Count
                    && frame.Adjustments[index].DamageType == "Burn"
                    && frame.Adjustments[index].Amount < 0
                    && frame.Adjustments[index].Pool is "Health" or "Shield"
                )
                {
                    var burnAdjustment = frame.Adjustments[index];
                    if (burnAdjustment.Pool == "Health")
                    {
                        health += burnAdjustment.Amount;
                        if (health <= 0)
                            terminalReached = true;
                    }
                    else if (groupWasAlive)
                    {
                        effective += -(long)burnAdjustment.Amount;
                    }
                    index++;
                }
                index--;
                continue;
            }
            if (adjustment.Pool != "Health")
                continue;
            if (adjustment.Amount <= 0)
            {
                health += adjustment.Amount;
                if (health <= 0)
                    terminalReached = true;
                continue;
            }

            var accepted = Math.Min(
                adjustment.Amount,
                Math.Max(0L, frame.After.HealthMax.Value - health)
            );
            health += accepted;
        }
        return effective;
    }

    private static long Gain(PeriodicFrameObservation frame, string type, string pool) =>
        frame
            .Adjustments.Where(adjustment =>
                adjustment.DamageType == type && adjustment.Pool == pool && adjustment.Amount > 0
            )
            .Sum(adjustment => (long)adjustment.Amount);
}

internal sealed record BattleInventorySummary(
    string BattleId,
    int Frames,
    int PeriodicFrames,
    IReadOnlyDictionary<string, int> StatSources,
    IReadOnlyDictionary<string, long> StatAmounts,
    int MissingStatSourceIdentities
);

internal sealed record PeriodicFrameObservation(
    string BattleId,
    int FrameIndex,
    string Combatant,
    AttributeSnapshot Before,
    AttributeSnapshot After,
    IReadOnlyList<AdjustmentObservation> Adjustments,
    IReadOnlyList<ActionObservation> Actions,
    IReadOnlyList<ActionObservation> ContextActions,
    bool HasDeathEvent
);

internal sealed record AttributeSnapshot(
    int? Burn,
    int? Poison,
    int? Regen,
    int? Health,
    int? HealthMax,
    int? Shield,
    int? FlatDamageReduction,
    int? PercentDamageReduction
);

internal sealed record AdjustmentObservation(
    string DamageType,
    string Pool,
    int Amount,
    bool IsCritical,
    bool IsDamageReduced
);

internal sealed record ActionObservation(
    string Action,
    string? ResolvedSourceId,
    string? DirectSourceId,
    string? TriggerSourceId
);
