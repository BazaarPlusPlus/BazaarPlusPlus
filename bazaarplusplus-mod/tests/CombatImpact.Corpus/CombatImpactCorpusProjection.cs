#nullable enable
using System.Diagnostics;
using System.Text;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarGameShared.Infra.Serialization;
using BazaarPlusPlus.Game.PostCombatImpact.Data;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

internal sealed class CombatImpactCorpusCatalog
{
    private readonly IReadOnlyDictionary<Guid, TCardBase> _cards;

    private CombatImpactCorpusCatalog(IReadOnlyDictionary<Guid, TCardBase> cards)
    {
        _cards = cards;
    }

    internal static CombatImpactCorpusCatalog Load(string databasePath)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("GameData.db was not found.", databasePath);

        var cards = new Dictionary<Guid, TCardBase>();
        var settings = new BazaarJsonSerializerSettings(validate: false);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Data FROM cards ORDER BY Id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!Guid.TryParse(reader.GetString(0), out var id))
                continue;
            var payload = reader.GetValue(1) switch
            {
                string text => text,
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                var value => Convert.ToString(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            };
            if (string.IsNullOrWhiteSpace(payload))
                continue;
            var card = JsonConvert.DeserializeObject<TCardBase>(payload!, settings);
            if (card != null)
                cards[id] = card;
        }

        if (cards.Count == 0)
            throw new InvalidOperationException("GameData.db yielded no card templates.");
        return new CombatImpactCorpusCatalog(cards);
    }

    internal CombatImpactCorpusEntityBuild BuildEntities(ReplayObservationInput replay)
    {
        var seeds = new Dictionary<string, CorpusCardSeed>(StringComparer.Ordinal);
        foreach (var spawned in replay.Spawn.Events.OfType<GameSimEventCardSpawned>())
        {
            if (!Guid.TryParse(spawned.TemplateId, out var templateId))
                continue;
            replay.Spawn.Cards.TryGetValue(spawned.InstanceId, out var update);
            seeds[spawned.InstanceId] = new CorpusCardSeed(
                spawned.InstanceId,
                templateId,
                spawned.Type,
                spawned.CombatantId,
                update?.Tier,
                update?.Enchantment,
                ReadAttributes(update)
            );
        }

        foreach (var snapshot in CombatImpactTransformedCardSnapshotReader.Read(replay.Combat))
        {
            if (!Guid.TryParse(snapshot.Card.TemplateId, out var templateId))
                continue;
            seeds.TryAdd(
                snapshot.Card.InstanceId,
                new CorpusCardSeed(
                    snapshot.Card.InstanceId,
                    templateId,
                    snapshot.Card.Type,
                    snapshot.Card.CombatantId,
                    snapshot.Update?.Tier,
                    snapshot.Update?.Enchantment,
                    ReadAttributes(snapshot.Update)
                )
            );
        }

        var entities = new Dictionary<string, CombatImpactEntity>(StringComparer.Ordinal);
        var order = 0;
        foreach (var seed in seeds.Values.OrderBy(seed => seed.InstanceId, StringComparer.Ordinal))
        {
            if (!_cards.TryGetValue(seed.TemplateId, out var template))
                continue;
            var tier = seed.Tier ?? template.StartingTier;
            var activeAbilities = ActiveAbilities(template, tier, seed.Enchantment);
            var activeAuras = template is IHasTierData auraTiered
                ? auraTiered.GetAuraTemplatesByTier(tier).ToList()
                : template.Auras.Values.ToList();
            if (
                seed.Enchantment is { } enchantment
                && template is TCardItem auraItem
                && auraItem.Enchantments?.TryGetValue(enchantment, out var enchantmentTemplate)
                    == true
            )
                activeAuras.AddRange(enchantmentTemplate.Auras.Values);
            var attributeTypes = ReadAbilityAttributeTypes(template, seed.Enchantment);
            entities[seed.InstanceId] = new CombatImpactEntity(
                seed.InstanceId,
                template.InternalName,
                seed.Type.ToString(),
                null,
                order++,
                seed.TemplateId,
                tier,
                EnchantmentType: seed.Enchantment,
                Attributes: seed.Attributes,
                CombatantId: seed.Combatant,
                AbilityAttributeTypesByEffectId: attributeTypes,
                PrerequisiteSkillSourceRulesByEffectId: CombatImpactAttributionRuleReader.ReadSourceRules(
                    activeAbilities,
                    []
                ),
                UseAttributionRules: seed.Type == ECardType.SocketEffect
                    ? CombatImpactAttributionRuleReader.ReadUseRules(activeAbilities)
                    : null,
                AbilityAttributeModifiersByEffectId: CombatImpactAbilityAttributeModifierReader.Read(
                    activeAbilities
                ),
                CriticalTriggerAbilitiesByEffectId: CombatImpactCriticalTriggerReader.Read(
                    activeAbilities
                ),
                AuraAttributeModifiersByEffectId: CombatImpactAbilityAttributeModifierReader.ReadAuras(
                    activeAuras
                ),
                ReferenceValuedAuraEffectIds: activeAuras
                    .Where(aura =>
                        aura.Action
                            is BazaarGameShared.Domain.Effect.AuraActions.TAuraActionCardModifyAttribute modifier
                        && modifier.Value
                            is BazaarGameShared.Domain.Values.ReferenceValues.ITReferenceValue
                    )
                    .Select(aura => aura.Id)
                    .ToArray()
            );
        }

        foreach (var combatant in new[] { ECombatantId.Player, ECombatantId.Opponent })
        {
            var id = CombatImpactProjector.PlayerId(combatant);
            entities[id] = new CombatImpactEntity(
                id,
                combatant.ToString(),
                "Player",
                null,
                order++,
                CombatantId: combatant
            );
        }
        var implicitPlayerEffects = CombatImpactImplicitPlayerEffectReader.AddMissing(
            replay.Combat,
            entities,
            _cards.Values
        );
        return new CombatImpactCorpusEntityBuild(entities, implicitPlayerEffects);
    }

    private static IReadOnlyDictionary<ECardAttributeType, int>? ReadAttributes(
        SimUpdateCard? update
    ) =>
        update == null
            ? null
            : update.Attributes.ToDictionary(item => item.Key, item => item.Value.Value);

    private static IReadOnlyDictionary<ECardAttributeType, int>? ReadAttributes(
        CombatSimCardUpdate? update
    ) =>
        update == null
            ? null
            : update.Attributes.ToDictionary(item => item.Key, item => item.Value.CurrentValue);

    private static IReadOnlyList<TCardAbility> ActiveAbilities(
        TCardBase template,
        ETier tier,
        EEnchantmentType? enchantment
    )
    {
        var abilities = template is IHasTierData tiered
            ? tiered.GetAbilityTemplatesByTier(tier).ToList()
            : template.Abilities.Values.ToList();
        if (
            enchantment.HasValue
            && template is TCardItem item
            && item.Enchantments?.TryGetValue(enchantment.Value, out var enchantmentTemplate)
                == true
        )
        {
            abilities.AddRange(enchantmentTemplate.Abilities.Values);
        }
        return abilities;
    }

    private static IReadOnlyDictionary<string, ECardAttributeType>? ReadAbilityAttributeTypes(
        TCardBase template,
        EEnchantmentType? enchantment
    )
    {
        var mappings = new Dictionary<string, ECardAttributeType>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        AddAbilityAttributeTypes(template.Abilities.Values, mappings, ambiguous);
        if (
            enchantment.HasValue
            && template is TCardItem item
            && item.Enchantments?.TryGetValue(enchantment.Value, out var enchantmentTemplate)
                == true
        )
        {
            AddAbilityAttributeTypes(enchantmentTemplate.Abilities.Values, mappings, ambiguous);
        }
        return mappings.Count == 0 ? null : mappings;
    }

    private static void AddAbilityAttributeTypes(
        IEnumerable<TCardAbility> abilities,
        IDictionary<string, ECardAttributeType> mappings,
        ISet<string> ambiguous
    )
    {
        foreach (var ability in abilities)
        {
            if (
                ability.Action is not TActionCardModifyAttribute modifier
                || string.IsNullOrWhiteSpace(ability.Id)
                || ambiguous.Contains(ability.Id)
            )
                continue;
            if (!mappings.TryGetValue(ability.Id, out var existing))
            {
                mappings[ability.Id] = modifier.AttributeType;
                continue;
            }
            if (existing == modifier.AttributeType)
                continue;
            mappings.Remove(ability.Id);
            ambiguous.Add(ability.Id);
        }
    }

    private sealed record CorpusCardSeed(
        string InstanceId,
        Guid TemplateId,
        ECardType Type,
        ECombatantId Combatant,
        ETier? Tier,
        EEnchantmentType? Enchantment,
        IReadOnlyDictionary<ECardAttributeType, int>? Attributes
    );
}

internal sealed record CombatImpactCorpusEntityBuild(
    IReadOnlyDictionary<string, CombatImpactEntity> Entities,
    int ImplicitPlayerEffects
);

internal static class CombatImpactCorpusProjection
{
    internal static CombatImpactCorpusProjectionResult Analyze(
        IReadOnlyList<ReplayObservationInput> replays,
        CombatImpactCorpusCatalog catalog,
        bool benchmarkEnabled
    )
    {
        var orderedReplays = replays.OrderBy(replay => replay.BattleId, StringComparer.Ordinal);
        IEnumerable<CombatImpactCorpusProjectionInput> orderedInputs;
        if (benchmarkEnabled)
        {
            var benchmarkInputs = orderedReplays
                .Select(replay => new CombatImpactCorpusProjectionInput(
                    replay,
                    catalog.BuildEntities(replay)
                ))
                .ToArray();
            orderedInputs = benchmarkInputs;
            if (benchmarkInputs.Length > 0)
            {
                CombatImpactProjector.Project(
                    benchmarkInputs[0].Replay.Combat,
                    benchmarkInputs[0].EntityBuild.Entities
                );
            }
        }
        else
            orderedInputs = orderedReplays.Select(replay => new CombatImpactCorpusProjectionInput(
                replay,
                catalog.BuildEntities(replay)
            ));

        var benchmarkObservations = benchmarkEnabled
            ? new List<CombatImpactProjectionBenchmarkObservation>(replays.Count)
            : null;
        var diagnostics = new List<CardAttributeAttributionCorpusObservation>();
        var rawExecutions = 0;
        var implicitPlayerEffects = 0;
        var criticalTriggerResolvedOrigins = 0;
        var criticalTriggerAttributedOrigins = 0;
        var criticalTriggerEvidenceFailures = new List<string>();
        foreach (var input in orderedInputs)
        {
            var replay = input.Replay;
            rawExecutions += replay.Combat.Frames.Sum(frame =>
                frame
                    .Events.OfType<CombatSimEventEffectExecuted>()
                    .Count(effect =>
                        effect.ActionType == EActionCommandType.CardModifyAttribute
                        && effect.Target is EffectTargetCard
                    )
            );
            var entityBuild = input.EntityBuild;
            var entities = entityBuild.Entities;
            implicitPlayerEffects += entityBuild.ImplicitPlayerEffects;
            var allocatedBefore = benchmarkEnabled ? GC.GetAllocatedBytesForCurrentThread() : 0;
            var startedAt = benchmarkEnabled ? Stopwatch.GetTimestamp() : 0;
            var report = CombatImpactProjector.Project(replay.Combat, entities);
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
        ReplayObservationInput replay,
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
        ReplayObservationInput replay,
        CombatImpactAttributeTransitionDiagnostic diagnostic,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        if (diagnostic.FrameIndex < 0 || diagnostic.FrameIndex >= replay.Combat.Frames.Count)
            return [];

        return replay
            .Combat.Frames[diagnostic.FrameIndex]
            .Events.OfType<CombatSimEventEffectExecuted>()
            .Where(effect =>
                effect.ActionType == EActionCommandType.CardModifyAttribute
                && effect.Target is EffectTargetCard target
                && string.Equals(target.Target.Value, diagnostic.TargetId, StringComparison.Ordinal)
            )
            .Select(effect => ReadClaimant(effect, entities))
            .ToArray();
    }

    private static CardAttributeClaimantCorpusObservation ReadClaimant(
        CombatSimEventEffectExecuted effect,
        IReadOnlyDictionary<string, CombatImpactEntity> entities
    )
    {
        var directSourceId = effect.Source?.Value;
        var triggerSourceId = effect.TriggerSource?.Value;
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
            modifier?.Value.GetType().Name
        );
    }

    private static string? ResolveModifierSource(
        string? directSourceId,
        string? triggerSourceId,
        string? effectId,
        IReadOnlyDictionary<string, CombatImpactEntity> entities,
        out TActionCardModifyAttribute? modifier
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

internal sealed record CombatImpactCorpusProjectionInput(
    ReplayObservationInput Replay,
    CombatImpactCorpusEntityBuild EntityBuild
);

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
