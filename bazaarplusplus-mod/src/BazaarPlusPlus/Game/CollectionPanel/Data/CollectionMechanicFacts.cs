#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Effect.Trigger;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Collection-owned structured facts projected once while a catalog VM is built. Filtering and
// view refreshes read only the resulting flags; they never revisit game effect graphs.
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

    public static bool TryFromHiddenTag(EHiddenTag hiddenTag, out CollectionMechanic mechanic)
    {
        // AbsorbDestroy is not on the keyword whitelist, so mapping it here only feeds the
        // Destroy mechanic fact — it never surfaces as its own keyword chip.
        mechanic = hiddenTag switch
        {
            EHiddenTag.Multicast => CollectionMechanic.Multicast,
            EHiddenTag.AbsorbDestroy => CollectionMechanic.Destroy,
            _ => CollectionMechanic.None,
        };
        return mechanic != CollectionMechanic.None;
    }

    private static CollectionMechanic FromNativeHiddenTags(
        IReadOnlyCollection<EHiddenTag> hiddenTags
    )
    {
        var facts = CollectionMechanic.None;
        foreach (var hiddenTag in hiddenTags)
            if (TryFromHiddenTag(hiddenTag, out var mechanic))
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
        // Direct destroy plus the rest of the destroy-cluster action surfaces. Nested abilities
        // carried inside TActionCardTransformDestroyed.Abilities belong to the spawned
        // replacement and are intentionally never walked (sub-rule retained from #149; no
        // longer separately observable through the single Destroy flag).
        if (action is TActionCardDestroy or TActionCardRepair or TActionCardTransformDestroyed)
            return CollectionMechanic.Destroy;
        if (action is not TActionAnd combined)
            return CollectionMechanic.None;

        var facts = CollectionMechanic.None;
        foreach (var child in combined.Actions)
            facts |= ProjectActionFacts(child);
        return facts;
    }

    // Destruction-reaction triggers only. TTriggerOnCardRepaired is deliberately excluded —
    // the "was repaired" trigger is not part of the destroy cluster (repair *actions* match
    // via ProjectActionFacts instead).
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
