#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Values.ReferenceValues;
using BazaarGameShared.Infra.Messages.CombatSimEvents;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactImplicitPlayerEffectReader
{
    internal static int AddMissing(
        CombatSim simulation,
        IDictionary<string, CombatImpactEntity> entities,
        IEnumerable<TCardBase> templates
    )
    {
        var observations = ReadObservations(simulation, entities);
        if (observations.Count == 0)
            return 0;

        var candidates = templates
            .Where(template => template.Type == ECardType.PlayerEffect)
            .Select(CreateCandidate)
            .Where(candidate => candidate != null)
            .Cast<ImplicitPlayerEffectCandidate>()
            .ToArray();
        var order = entities.Values.Select(entity => entity.Order).DefaultIfEmpty(-1).Max() + 1;
        var added = 0;
        foreach (var observation in observations.Values.OrderBy(item => item.SourceId))
        {
            if (
                observation.HasUnsupportedExecution
                || observation.ModifierEffectIds.Count + observation.AuraEffectIds.Count < 2
            )
                continue;

            // Combat-only PlayerEffects carry no template id in replay payloads. Reconstruct
            // one only when the complete observed effect signature selects exactly one static
            // PlayerEffect template; ambiguity or a partial signature safely stays unresolved.
            var matches = candidates
                .Where(candidate => candidate.Matches(observation))
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
                continue;

            var combatants = observation
                .TriggerSourceIds.Select(id =>
                    entities.TryGetValue(id, out var trigger) ? trigger.CombatantId : null
                )
                .Where(combatant => combatant.HasValue)
                .Select(combatant => combatant!.Value)
                .Distinct()
                .ToArray();
            if (combatants.Length != 1)
                continue;

            entities[observation.SourceId] = CreateEntity(
                observation.SourceId,
                matches[0],
                combatants[0],
                order++
            );
            added++;
        }

        return added;
    }

    private static Dictionary<string, ImplicitPlayerEffectObservation> ReadObservations(
        CombatSim simulation,
        IDictionary<string, CombatImpactEntity> entities
    )
    {
        var observations = new Dictionary<string, ImplicitPlayerEffectObservation>(
            StringComparer.Ordinal
        );
        foreach (var frame in simulation.Frames)
        {
            foreach (var effect in frame.Events.OfType<CombatSimEventEffectExecuted>())
            {
                var sourceId = effect.Source?.Value;
                if (string.IsNullOrWhiteSpace(sourceId) || entities.ContainsKey(sourceId!))
                    continue;

                var observation = GetOrAdd(observations, sourceId!);
                AddTriggerSource(observation, effect.TriggerSource?.Value);
                if (
                    effect.ActionType != EActionCommandType.CardModifyAttribute
                    || string.IsNullOrWhiteSpace(effect.EffectId)
                )
                {
                    observation.HasUnsupportedExecution = true;
                    continue;
                }
                observation.ModifierEffectIds.Add(effect.EffectId);
            }

            foreach (var aura in frame.Events.OfType<CombatSimEventEffectAuraExecuted>())
            {
                var sourceId = aura.Source?.Value;
                if (
                    string.IsNullOrWhiteSpace(sourceId)
                    || entities.ContainsKey(sourceId!)
                    || string.IsNullOrWhiteSpace(aura.EffectId)
                )
                    continue;

                var observation = GetOrAdd(observations, sourceId!);
                observation.AuraEffectIds.Add(aura.EffectId);
                AddTriggerSource(observation, aura.TriggerSource?.Value);
            }
        }
        return observations;
    }

    private static ImplicitPlayerEffectObservation GetOrAdd(
        IDictionary<string, ImplicitPlayerEffectObservation> observations,
        string sourceId
    )
    {
        if (!observations.TryGetValue(sourceId, out var observation))
        {
            observation = new ImplicitPlayerEffectObservation(sourceId);
            observations[sourceId] = observation;
        }
        return observation;
    }

    private static void AddTriggerSource(
        ImplicitPlayerEffectObservation observation,
        string? sourceId
    )
    {
        if (!string.IsNullOrWhiteSpace(sourceId))
            observation.TriggerSourceIds.Add(sourceId!);
    }

    private static ImplicitPlayerEffectCandidate? CreateCandidate(TCardBase template)
    {
        var tier = template.StartingTier;
        var abilities = ReadActiveAbilities(template, tier);
        if (abilities.Any(ability => ability.Action is not TActionCardModifyAttribute))
            return null;

        var auras = ReadActiveAuras(template, tier);
        var modifierEffectIds = abilities
            .Select(ability => ability.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var auraEffectIds = auras
            .Select(aura => aura.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (modifierEffectIds.Count + auraEffectIds.Count < 2)
            return null;

        return new ImplicitPlayerEffectCandidate(
            template,
            tier,
            abilities,
            auras,
            modifierEffectIds,
            auraEffectIds
        );
    }

    private static IReadOnlyList<TCardAbility> ReadActiveAbilities(TCardBase template, ETier tier)
    {
        try
        {
            if (template is IHasTierData tiered)
                return tiered.GetAbilityTemplatesByTier(tier).ToArray();
        }
        catch
        {
            // Fall through to the complete template graph when tier data is incomplete.
        }
        return template.Abilities.Values.ToArray();
    }

    private static IReadOnlyList<TCardAura> ReadActiveAuras(TCardBase template, ETier tier)
    {
        try
        {
            if (template is IHasTierData tiered)
                return tiered.GetAuraTemplatesByTier(tier).ToArray();
        }
        catch
        {
            // Fall through to the complete template graph when tier data is incomplete.
        }
        return template.Auras.Values.ToArray();
    }

    private static CombatImpactEntity CreateEntity(
        string instanceId,
        ImplicitPlayerEffectCandidate candidate,
        ECombatantId combatant,
        int order
    )
    {
        var template = candidate.Template;
        var modifiers = CombatImpactAbilityAttributeModifierReader.Read(candidate.Abilities);
        var abilityAttributeTypes = modifiers?.ToDictionary(
            item => item.Key,
            item => item.Value.AttributeType,
            StringComparer.Ordinal
        );
        var auraAttributes = ReadAuraAttributes(candidate.Auras);
        return new CombatImpactEntity(
            instanceId,
            template.InternalName,
            template.Type.ToString(),
            null,
            order,
            template.Id,
            candidate.Tier,
            Attributes: ReadAttributes(template, candidate.Tier),
            CombatantId: combatant,
            AbilityAttributeTypesByEffectId: abilityAttributeTypes,
            AuraAttributeTypesByEffectId: auraAttributes.Types,
            ReferenceValuedAuraEffectIds: auraAttributes.ReferenceValuedEffectIds,
            PrerequisiteSkillSourceRulesByEffectId: CombatImpactAttributionRuleReader.ReadSourceRules(
                candidate.Abilities,
                candidate.Auras
            ),
            AbilityAttributeModifiersByEffectId: modifiers,
            CriticalTriggerAbilitiesByEffectId: CombatImpactCriticalTriggerReader.Read(
                candidate.Abilities
            )
        );
    }

    private static IReadOnlyDictionary<ECardAttributeType, int>? ReadAttributes(
        TCardBase template,
        ETier tier
    )
    {
        if (template is not IHasTierData tiered)
            return null;

        var attributes = Enum.GetValues(typeof(ECardAttributeType))
            .Cast<ECardAttributeType>()
            .Select(attribute =>
                (Attribute: attribute, Value: tiered.GetAttributeBaseValueAtTier(attribute, tier))
            )
            .Where(item => item.Value.HasValue)
            .ToDictionary(item => item.Attribute, item => item.Value!.Value);
        return attributes.Count == 0 ? null : attributes;
    }

    private static AuraAttributes ReadAuraAttributes(IReadOnlyList<TCardAura> auras)
    {
        var types = new Dictionary<string, ECardAttributeType>(StringComparer.Ordinal);
        var referenceValued = new HashSet<string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var aura in auras)
        {
            if (
                aura.Action is not TAuraActionCardModifyAttribute modifier
                || string.IsNullOrWhiteSpace(aura.Id)
                || ambiguous.Contains(aura.Id)
            )
                continue;
            if (types.TryGetValue(aura.Id, out var existing) && existing != modifier.AttributeType)
            {
                types.Remove(aura.Id);
                referenceValued.Remove(aura.Id);
                ambiguous.Add(aura.Id);
                continue;
            }

            types[aura.Id] = modifier.AttributeType;
            if (modifier.Value is ITReferenceValue)
                referenceValued.Add(aura.Id);
        }

        return new AuraAttributes(
            types.Count == 0 ? null : types,
            referenceValued.Count == 0 ? null : referenceValued
        );
    }

    private sealed class ImplicitPlayerEffectObservation(string sourceId)
    {
        internal string SourceId { get; } = sourceId;
        internal HashSet<string> ModifierEffectIds { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> AuraEffectIds { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> TriggerSourceIds { get; } = new(StringComparer.Ordinal);
        internal bool HasUnsupportedExecution { get; set; }
    }

    private sealed record ImplicitPlayerEffectCandidate(
        TCardBase Template,
        ETier Tier,
        IReadOnlyList<TCardAbility> Abilities,
        IReadOnlyList<TCardAura> Auras,
        HashSet<string> ModifierEffectIds,
        HashSet<string> AuraEffectIds
    )
    {
        internal bool Matches(ImplicitPlayerEffectObservation observation) =>
            ModifierEffectIds.SetEquals(observation.ModifierEffectIds)
            && AuraEffectIds.SetEquals(observation.AuraEffectIds);
    }

    private readonly record struct AuraAttributes(
        IReadOnlyDictionary<string, ECardAttributeType>? Types,
        IReadOnlyCollection<string>? ReferenceValuedEffectIds
    );
}
