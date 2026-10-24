#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.PostCombatImpact.Data;

internal static class CombatImpactEntityTags
{
    internal static IReadOnlyCollection<CardHiddenTag>? ResolveHiddenTags(Card card)
    {
        IReadOnlyCollection<EHiddenTag>? enchantmentHiddenTags = null;
        if (
            card is ItemCard { Enchantment: { } enchantment }
            && card.Template is TCardItem { Enchantments: not null } itemTemplate
            && itemTemplate.Enchantments.TryGetValue(enchantment, out var enchantmentTemplate)
        )
            enchantmentHiddenTags = enchantmentTemplate?.HiddenTags;

        return CombatImpactHiddenTags.Merge(
            HiddenTags(card.HiddenTags),
            HiddenTags(card.Template?.HiddenTags),
            HiddenTags(enchantmentHiddenTags)
        );
    }

    internal static IReadOnlyCollection<CardTag>? ResolveTags(Card card)
    {
        IReadOnlyCollection<ECardTag>? enchantmentTags = null;
        if (
            card is ItemCard { Enchantment: { } enchantment }
            && card.Template is TCardItem { Enchantments: not null } itemTemplate
            && itemTemplate.Enchantments.TryGetValue(enchantment, out var enchantmentTemplate)
        )
            enchantmentTags = enchantmentTemplate?.Tags;

        return CombatImpactTags.Merge(
            Tags(card.Tags),
            Tags(card.Template?.Tags),
            Tags(enchantmentTags)
        );
    }

    /// <summary>The card facets of the game's tags, in the game's order.</summary>
    internal static IReadOnlyCollection<CardTag>? Tags(IEnumerable<ECardTag>? tags) =>
        tags == null ? null : DesktopCardFacets.Tag.ToFacetSet(tags);

    internal static IReadOnlyCollection<CardHiddenTag>? HiddenTags(
        IEnumerable<EHiddenTag>? hiddenTags
    ) => hiddenTags == null ? null : DesktopCardFacets.HiddenTag.ToFacetSet(hiddenTags);
}
