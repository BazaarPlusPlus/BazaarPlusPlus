#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.ItemEnchantPreview.Preview;
using BazaarPlusPlus.Localization;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewService
{
    public static List<TooltipSegment> BuildPreviewSegments(
        Card card,
        IReadOnlyCollection<string>? restrictToEnchantmentNames = null
    )
    {
        return BuildPreviewSegments(card as ItemCard, restrictToEnchantmentNames);
    }

    public static List<TooltipSegment> BuildPreviewSegments(
        ItemCard? itemCard,
        IReadOnlyCollection<string>? restrictToEnchantmentNames = null
    )
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
            enchantments.Keys,
            restrictToEnchantmentNames
        );

        var languageCode = L.CurrentLanguageCode;
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
            var cacheKey = ItemEnchantPreviewCache.CreateKey(snapshot, languageCode);
            if (ItemEnchantPreviewCache.TryAppend(cacheKey, segments))
                continue;

            var previewCard = ItemEnchantPreviewCardCloneFactory.Create(itemCard, snapshot);
            var renderedSegments = ItemEnchantPreviewRenderer.Render(previewCard, enchantment);

            ItemEnchantPreviewCache.Save(cacheKey, renderedSegments);
            segments.AddRange(renderedSegments);
        }

        return segments;
    }
}
