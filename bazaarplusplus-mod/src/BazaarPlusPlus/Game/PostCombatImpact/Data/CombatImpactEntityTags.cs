#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactEntityTags
{
    internal static IReadOnlyCollection<EHiddenTag>? ResolveHiddenTags(Card card)
    {
        IReadOnlyCollection<EHiddenTag>? enchantmentHiddenTags = null;
        if (
            card is ItemCard { Enchantment: { } enchantment }
            && card.Template is TCardItem { Enchantments: not null } itemTemplate
            && itemTemplate.Enchantments.TryGetValue(enchantment, out var enchantmentTemplate)
        )
            enchantmentHiddenTags = enchantmentTemplate?.HiddenTags;

        return CombatImpactHiddenTags.Merge(
            card.HiddenTags,
            card.Template?.HiddenTags,
            enchantmentHiddenTags
        );
    }

    internal static IReadOnlyCollection<ECardTag>? ResolveTags(Card card)
    {
        IReadOnlyCollection<ECardTag>? enchantmentTags = null;
        if (
            card is ItemCard { Enchantment: { } enchantment }
            && card.Template is TCardItem { Enchantments: not null } itemTemplate
            && itemTemplate.Enchantments.TryGetValue(enchantment, out var enchantmentTemplate)
        )
            enchantmentTags = enchantmentTemplate?.Tags;

        return CombatImpactTags.Merge(card.Tags, card.Template?.Tags, enchantmentTags);
    }
}
