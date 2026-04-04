using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.ItemEnchantPreview.Preview;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewService
{
    public static List<TooltipSegment> BuildPreviewSegments(Card card)
    {
        return BuildPreviewSegments(card as ItemCard);
    }

    public static List<TooltipSegment> BuildPreviewSegments(ItemCard itemCard)
    {
        var empty = new List<TooltipSegment>();
        if (
            itemCard == null
            || !ItemEnchantPreviewEligibility.IsEligible(itemCard, Data.IsInCombat)
        )
            return empty;

        var enchantments = itemCard.GetEnchantments();
        if (enchantments == null || enchantments.Count == 0)
            return empty;

        var candidates = ItemEnchantPreviewCandidateSelector.SelectCandidates(
            itemCard.Enchantment,
            enchantments.Keys
        );

        var segments = new List<TooltipSegment>();
        foreach (var enchantmentType in candidates)
        {
            if (!enchantments.TryGetValue(enchantmentType, out var enchantment))
                continue;

            var snapshot = ItemEnchantPreviewSnapshotFactory.Create(
                itemCard,
                enchantmentType,
                enchantment
            );
            if (ItemEnchantPreviewCache.TryGet(snapshot, out var cachedSegments))
            {
                segments.AddRange(cachedSegments);
                continue;
            }

            var previewCard = ItemEnchantPreviewCardCloneFactory.Create(itemCard, snapshot);
            var renderedSegments = ItemEnchantPreviewRenderer.Render(previewCard, enchantment);

            ItemEnchantPreviewCache.Save(snapshot, renderedSegments);
            segments.AddRange(renderedSegments);
        }

        return segments;
    }
}
