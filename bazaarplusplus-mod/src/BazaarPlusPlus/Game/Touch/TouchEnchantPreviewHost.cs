#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.Mobile.Previews;
using TheBazaar;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The Mono host's card tooltip for the Mobile UI's enchant preview
/// (<see cref="TouchEnchantPreview{TTooltip}"/>) on a mobile platform (ADR-0025): the item's other
/// enchantments and their effects as the game renders them (<see cref="TouchCardText"/>), and the
/// upgrade preview, <see cref="TouchUpgradePreview"/>'s. The composition binds
/// <see cref="Preview"/>, which <c>Patches/Tooltips/TouchEnchantPreviewPatches</c> and
/// <see cref="TouchTooltipPreviewHost"/> read. Main thread only.
/// </summary>
internal sealed class TouchEnchantPreviewHost : IEnchantPreviewHost<CardTooltipData>
{
    /// <summary>The enchant preview on a mobile platform; null on the desktop.</summary>
    internal static TouchEnchantPreview<CardTooltipData>? Preview { get; set; }

    public IReadOnlyList<string> Candidates(CardTooltipData tooltip)
    {
        if (Data.IsInCombat || AppState.CurrentState is ReplayState)
            return Array.Empty<string>();
        if (
            tooltip.CardInstance is not ItemCard item
            || tooltip.CardTemplate is not TCardItem template
            || template.Enchantments == null
            || !IsEligibleSection(item)
        )
            return Array.Empty<string>();
        var current = item.Enchantment;
        var result = new List<EEnchantmentType>();
        foreach (var entry in template.Enchantments)
            if (entry.Value != null && (!current.HasValue || entry.Key != current.Value))
                result.Add(entry.Key);
        return result.OrderBy(type => (int)type).Select(type => type.ToString()).ToArray();
    }

    public bool HoldsUpgrade(CardTooltipData tooltip) =>
        tooltip.CardInstance is ItemCard upgradable
        && upgradable.CanCardUpgrade()
        && TouchUpgradePreview.Holds(upgradable);

    public string? Effect(CardTooltipData tooltip, string enchantment) =>
        Enum.TryParse<EEnchantmentType>(enchantment, out var type)
            ? TouchCardText.RenderEffect(tooltip, type)
            : null;

    public IReadOnlyList<CollectionTierTooltipText> TierEffects(
        CardTooltipData tooltip,
        string enchantment
    )
    {
        var effects = new List<CollectionTierTooltipText>();
        if (!Enum.TryParse<EEnchantmentType>(enchantment, out var type))
            return effects;
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
        return effects;
    }

    public string Label(string enchantment)
    {
        try
        {
            return new TheBazaar.Utilities.LocalizableText(enchantment).GetLocalizedText();
        }
        catch
        {
            return enchantment;
        }
    }

    // The desktop's ItemEnchantPreviewEligibility: an item on a board or in a stash, or one no
    // player owns yet, such as a shop's.
    private static bool IsEligibleSection(ItemCard item) =>
        item.Section is EInventorySection.Hand or EInventorySection.Stash || item.Owner == null;
}
