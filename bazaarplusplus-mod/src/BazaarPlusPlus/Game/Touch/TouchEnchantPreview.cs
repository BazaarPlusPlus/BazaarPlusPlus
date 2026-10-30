#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.GameInterop.Cards;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The enchantment effects a live item's touch inspect lists, as the Android host's
/// <c>AndroidEnchantPreview</c>: each enchantment the item does not have, its effect from the
/// item's tier up, through the game's own lock-mode preview card (<see cref="TouchCardText"/>).
/// </summary>
internal static class TouchEnchantPreview
{
    public static IReadOnlyList<EEnchantmentType> Candidates(CardTooltipData tooltip)
    {
        if (Data.IsInCombat || AppState.CurrentState is ReplayState)
            return Array.Empty<EEnchantmentType>();
        if (
            tooltip.CardInstance is not ItemCard item
            || tooltip.CardTemplate is not TCardItem template
            || template.Enchantments == null
            || !IsEligibleSection(item)
        )
            return Array.Empty<EEnchantmentType>();
        var current = item.Enchantment;
        var result = new List<EEnchantmentType>();
        foreach (var entry in template.Enchantments)
            if (entry.Value != null && (!current.HasValue || entry.Key != current.Value))
                result.Add(entry.Key);
        return result.OrderBy(type => (int)type).ToArray();
    }

    public static IReadOnlyList<(string Label, string Text)> Build(
        CardTooltipData tooltip,
        IReadOnlyList<EEnchantmentType> candidates
    )
    {
        var lines = new List<(string, string)>();
        foreach (var type in candidates)
        {
            var effects = new List<CollectionTierTooltipText>();
            var current = tooltip.CardInstance.Tier;
            var last = current == ETier.Legendary ? current : ETier.Diamond;
            for (var tier = current; tier <= last; tier++)
            {
                var effect = TouchCardText.RenderEffect(tooltip, type, tier);
                if (
                    !string.IsNullOrWhiteSpace(effect)
                    && DesktopCardFacets.Tier.TryToFacet(tier, out var facet)
                )
                    effects.Add(new CollectionTierTooltipText(facet, effect!));
            }
            if (effects.Count > 0)
                lines.Add(
                    (
                        $"<color=#{ItemEnchantPreviewText.ColorHex(type.ToString())}>{Label(type)}</color>",
                        CollectionTierTooltipTextMerger.MergeLines(
                            effects,
                            keepValuesTogether: true
                        )
                    )
                );
        }
        return lines;
    }

    // The desktop's ItemEnchantPreviewEligibility: an item on a board or in a stash, or one no
    // player owns yet, such as a shop's.
    private static bool IsEligibleSection(ItemCard item) =>
        item.Section is EInventorySection.Hand or EInventorySection.Stash || item.Owner == null;

    private static string Label(EEnchantmentType type)
    {
        var name = type.ToString();
        try
        {
            return new TheBazaar.Utilities.LocalizableText(name).GetLocalizedText();
        }
        catch
        {
            return name;
        }
    }
}
