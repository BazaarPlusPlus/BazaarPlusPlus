#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Effect.Trigger;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal static class CollectionMechanicFacts
{
    private const CollectionMechanic AllAbilityMechanics =
        CollectionMechanic.Multicast | CollectionMechanic.Destroy;

    public static CollectionMechanic Project(TCardBase template)
    {
        var facts = FromNativeHiddenTags(template.HiddenTags);
        if (template is not IHasTierData tierData)
            return facts;

        var activeAbilityIds = new HashSet<string>(StringComparer.Ordinal);
        var activeAuraIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in tierData.Tiers)
        {
            var tier = pair.Key;
            var tierTemplate = pair.Value;
            if (tierData.GetAttributeBaseValueAtTier(ECardAttributeType.Multicast, tier) > 1)
                facts |= CollectionMechanic.Multicast;
            // Base destroy immunity only — enchantment-provided Radiant immunity lives on
            // Enchantments and is never walked here.
            if (tierData.GetAttributeBaseValueAtTier(ECardAttributeType.DestroyImmunity, tier) > 0)
                facts |= CollectionMechanic.Destroy;

            if (tierTemplate == null)
                continue;
            activeAbilityIds.UnionWith(tierTemplate.AbilityIds);
            activeAuraIds.UnionWith(tierTemplate.AuraIds);
        }

        foreach (var abilityId in activeAbilityIds)
        {
            if (template.Abilities.TryGetValue(abilityId, out var ability) && ability != null)
            {
                facts |= ProjectActionFacts(ability.Action);
                facts |= ProjectTriggerFacts(ability.Trigger);
            }

            if ((facts & AllAbilityMechanics) == AllAbilityMechanics)
                break;
        }

        if (!facts.Has(CollectionMechanic.Multicast))
        {
            foreach (var auraId in activeAuraIds)
            {
                if (
                    template.Auras.TryGetValue(auraId, out var aura)
                    && aura?.Action is TAuraActionCardModifyAttribute modifier
                    && modifier.AttributeType == ECardAttributeType.Multicast
                )
                {
                    facts |= CollectionMechanic.Multicast;
                    break;
                }
            }
        }

        return facts;
    }

    private static CollectionMechanic FromNativeHiddenTags(
        IReadOnlyCollection<EHiddenTag> hiddenTags
    )
    {
        var facts = CollectionMechanic.None;
        foreach (var hiddenTag in hiddenTags)
            if (
                DesktopCardFacets.HiddenTag.TryToFacet(hiddenTag, out var facet)
                && CollectionMechanics.TryFromHiddenTag(facet, out var mechanic)
            )
                facts |= mechanic;
        return facts;
    }

    private static CollectionMechanic ProjectActionFacts(ITAction? action)
    {
        if (
            action is TActionCardModifyAttribute modifier
            && modifier.AttributeType == ECardAttributeType.Multicast
        )
            return CollectionMechanic.Multicast;
        // Nested transform abilities belong to the spawned replacement card.
        if (action is TActionCardDestroy or TActionCardRepair or TActionCardTransformDestroyed)
            return CollectionMechanic.Destroy;
        if (action is not TActionAnd combined)
            return CollectionMechanic.None;

        var facts = CollectionMechanic.None;
        foreach (var child in combined.Actions)
            facts |= ProjectActionFacts(child);
        return facts;
    }

    // TTriggerOnCardRepaired is outside the destroy cluster. Repair actions still qualify.
    private static CollectionMechanic ProjectTriggerFacts(TTriggerBase? trigger)
    {
        if (
            trigger
            is TTriggerOnBeforeCardDestroyed
                or TTriggerOnCardDestroyed
                or TTriggerOnCardPerformedDestruction
        )
            return CollectionMechanic.Destroy;
        if (trigger is not TTriggerOr combined)
            return CollectionMechanic.None;

        var facts = CollectionMechanic.None;
        foreach (var child in combined.Triggers)
            facts |= ProjectTriggerFacts(child);
        return facts;
    }
}
