#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.GameInterop.Cards;

internal static class DesktopEffectiveCardTags
{
    internal static EffectiveCardTags Read(Card card)
    {
        IEnumerable<ECardTag>? enchantmentTags = null;
        IEnumerable<EHiddenTag>? enchantmentHiddenTags = null;
        if (
            card is ItemCard { Enchantment: { } active }
            && card.Template is TCardItem { Enchantments: not null } itemTemplate
            && itemTemplate.Enchantments.TryGetValue(active, out var enchantment)
        )
        {
            enchantmentTags = enchantment?.Tags;
            enchantmentHiddenTags = enchantment?.HiddenTags;
        }

        return new EffectiveCardTags(
            Tags(card.Tags),
            Tags(card.Template?.Tags),
            Tags(enchantmentTags),
            HiddenTags(card.HiddenTags),
            HiddenTags(card.Template?.HiddenTags),
            HiddenTags(enchantmentHiddenTags)
        );
    }

    private static IReadOnlyCollection<CardTag>? Tags(IEnumerable<ECardTag>? source) =>
        source == null ? null : DesktopCardFacets.Tag.ToFacetSet(source);

    private static IReadOnlyCollection<CardHiddenTag>? HiddenTags(
        IEnumerable<EHiddenTag>? source
    ) => source == null ? null : DesktopCardFacets.HiddenTag.ToFacetSet(source);
}
