#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class UpgradePreviewAttributeMultiplierResolver
{
    internal readonly record struct Multipliers(float Current, float Projected);

    internal static IReadOnlyDictionary<ECardAttributeType, Multipliers> Resolve(
        ItemCard source,
        ItemCard projected,
        ValueContext sourceValueContext
    )
    {
        var multipliers = new Dictionary<ECardAttributeType, Multipliers>();
        foreach (var auraSource in EnumerateAuraSources(source, sourceValueContext))
        {
            Accumulate(source, auraSource, sourceValueContext, projectedValue: false, multipliers);

            var projectedAuraSource = ReferenceEquals(auraSource, source) ? projected : auraSource;
            Accumulate(
                projected,
                projectedAuraSource,
                sourceValueContext,
                projectedValue: true,
                multipliers
            );
        }

        return multipliers;
    }

    private static void Accumulate(
        ItemCard target,
        Card auraSource,
        ValueContext sourceValueContext,
        bool projectedValue,
        Dictionary<ECardAttributeType, Multipliers> multipliers
    )
    {
        foreach (var aura in EnumerateActiveAuras(auraSource))
        {
            if (
                aura.Action
                    is not TAuraActionCardModifyAttribute
                    {
                        AttributeType: var auraAttribute,
                        Operation: EAttributeModifierOperation.Multiply,
                    } action
                || !IsActiveOutOfCombat(auraSource, aura, sourceValueContext)
                || !Targets(target, auraSource, action, sourceValueContext)
            )
                continue;

            var value = ResolveValue(action, auraSource, sourceValueContext);
            var current = multipliers.GetValueOrDefault(auraAttribute, new Multipliers(1f, 1f));
            multipliers[auraAttribute] = projectedValue
                ? current with
                {
                    Projected = current.Projected * value,
                }
                : current with
                {
                    Current = current.Current * value,
                };
        }
    }

    private static IEnumerable<Card> EnumerateAuraSources(
        ItemCard source,
        ValueContext sourceValueContext
    )
    {
        yield return source;

        var run = sourceValueContext.Run;
        if (run == null)
            yield break;

        var seen = new HashSet<InstanceId> { source.InstanceId };
        foreach (var player in new[] { run.GetPlayer(), run.GetOpponent() })
        {
            if (player == null)
                continue;

            foreach (
                var card in player
                    .Hand.GetItemsAsEnumerable()
                    .Concat(player.Stash.GetItemsAsEnumerable())
                    .Concat(player.Socket.GetItemsAsEnumerable())
                    .Concat(player.GetSkills())
                    .OfType<Card>()
            )
            {
                if (seen.Add(card.InstanceId))
                    yield return card;
            }
        }
    }

    private static IEnumerable<TCardAura> EnumerateActiveAuras(Card card)
    {
        if (card.Template is IHasTierData tierData)
        {
            foreach (var aura in tierData.GetAuraTemplatesByTier(card.Tier))
                yield return aura;
        }
        else if (card.Template != null)
        {
            foreach (var aura in card.Template.Auras.Values)
                yield return aura;
        }

        if (
            card is ItemCard { Enchantment: not null } item
            && item.Template is TCardItem itemTemplate
            && itemTemplate.TryGetEnchantmentTemplate(item.Enchantment.Value, out var enchantment)
            && enchantment != null
        )
        {
            foreach (var aura in enchantment.Auras.Values)
                yield return aura;
        }

        if (card is not ItemCard questItem || questItem.Template is not TCardItem questTemplate)
            yield break;

        foreach (
            var reward in (questTemplate.Quests ?? [])
                .SelectMany(group => group.Entries)
                .Where(entry => entry.IsComplete(questItem))
                .Select(entry => entry.Reward)
                .Where(reward => reward?.HasAuras == true)
        )
        {
            foreach (var aura in reward!.Auras.Values)
                yield return aura;
        }
    }

    private static bool IsActiveOutOfCombat(
        Card auraSource,
        TCardAura aura,
        ValueContext sourceValueContext
    )
    {
        if ((aura.WorksIn & EEffectWorksIn.OutOfCombatOnly) == 0)
            return false;

        if (
            auraSource.Type == ECardType.Item
            && (
                aura.ActiveIn switch
                {
                    EEffectActiveIn.HandOnly => auraSource.Section != EInventorySection.Hand,
                    EEffectActiveIn.StashOnly => auraSource.Section != EInventorySection.Stash,
                    EEffectActiveIn.HandAndStash => auraSource.Section == null,
                    _ => false,
                }
            )
        )
            return false;

        return aura.Prerequisites == null
            || aura.Prerequisites.All(prerequisite =>
                prerequisite.IsSatisfiedBy(
                    new PrereqContext(
                        sourceValueContext.Run,
                        auraSource,
                        sourceValueContext.EventContext
                    )
                )
            );
    }

    private static bool Targets(
        ItemCard target,
        Card auraSource,
        TAuraActionCardModifyAttribute action,
        ValueContext sourceValueContext
    )
    {
        if (sourceValueContext.Run == null)
            return ReferenceEquals(auraSource, target) && action.Target is TTargetCardSelf;

        var context = new TargetingContext(
            sourceValueContext.Run,
            auraSource,
            sourceValueContext.EventContext
        );
        return action
            .Target.GetTargets(context)
            .Any(card => card.GetInstanceId() == target.InstanceId);
    }

    private static float ResolveValue(
        TAuraActionCardModifyAttribute action,
        Card auraSource,
        ValueContext sourceValueContext
    ) =>
        action.Value.GetValue(
            new ValueContext(sourceValueContext.Run, auraSource, sourceValueContext.EventContext)
        );
}
