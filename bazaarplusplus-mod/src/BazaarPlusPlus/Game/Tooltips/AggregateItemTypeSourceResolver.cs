#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
using BazaarGameShared.Domain.Effect.Actions;
using BazaarGameShared.Domain.Effect.AuraActions;
using BazaarGameShared.Domain.Targeting;
using BazaarGameShared.Domain.Values.ReferenceValues;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class AggregateItemTypeSourceResolver
{
    internal static bool TryResolve(TCardBase template, out AggregateItemTypeSource source) =>
        TryResolve(template, null, out source);

    internal static bool TryResolve(
        TCardBase template,
        EEnchantmentType? activeEnchantment,
        out AggregateItemTypeSource source
    )
    {
        if (TryResolve(template.Auras.Values, template.Abilities.Values, out source))
            return true;

        if (
            activeEnchantment.HasValue
            && template is TCardItem itemTemplate
            && itemTemplate.TryGetEnchantmentTemplate(
                activeEnchantment.Value,
                out var enchantmentTemplate
            )
            && enchantmentTemplate != null
            && TryResolve(
                enchantmentTemplate.Auras.Values,
                enchantmentTemplate.Abilities.Values,
                out source
            )
        )
            return true;

        source = default;
        return false;
    }

    private static bool TryResolve(
        IEnumerable<TCardAura> auras,
        IEnumerable<TCardAbility> abilities,
        out AggregateItemTypeSource source
    )
    {
        foreach (var aura in auras)
        {
            if (aura.Action is TAuraActionCardAddTagsBySource)
            {
                source = AggregateItemTypeSource.LiveCard;
                return true;
            }
        }

        foreach (var ability in abilities)
        {
            if (ability.Action is TActionCardAddTagsBySource)
            {
                source = AggregateItemTypeSource.LiveCard;
                return true;
            }
        }

        // Forklift, Laurel's Fortress, enchanted Rowboat, and future equivalents
        // aggregate distinct types directly from inventory without copying them.
        foreach (var aura in auras)
        {
            if (
                aura.Action is TAuraActionCardModifyAttribute
                {
                    Value: TReferenceValueCardTagCount
                    {
                        Distinct: true,
                        Target: TTargetCardSection target
                    }
                }
            )
            {
                source = new AggregateItemTypeSource(target.TargetSection, target.ExcludeSelf);
                return true;
            }
        }

        source = default;
        return false;
    }
}

internal readonly record struct AggregateItemTypeSource(
    ETargetCardSectionTargetSection? Section,
    bool ExcludeSelf
)
{
    public static AggregateItemTypeSource LiveCard => new(null, false);
}
