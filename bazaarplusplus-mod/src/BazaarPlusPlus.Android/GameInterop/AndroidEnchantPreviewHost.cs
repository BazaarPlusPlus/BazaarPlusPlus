using System.Text;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.Mobile.Previews;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameClient.Domain.Tooltips;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppBazaarGameShared.Domain.Runs;
using Il2CppBazaarGameShared.Domain.Values;
using Il2CppTheBazaar;
using Il2CppTheBazaar.Tooltips;
using Il2CppTheBazaar.Utilities;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Android host's card tooltip for the Mobile UI's enchant preview
/// (<see cref="TouchEnchantPreview{TTooltip}"/>): the item's other enchantments and their effects
/// as the game renders them (<see cref="RenderEffect"/>, which Collection also renders with), and
/// the upgrade preview, <see cref="AndroidUpgradePreview"/>'s. Main thread only.
/// </summary>
internal sealed class AndroidEnchantPreviewHost : IEnchantPreviewHost<CardTooltipData>
{
    public IReadOnlyList<string> Candidates(CardTooltipData tooltip)
    {
        if (Data.IsInCombat || AppState.CurrentState?.TryCast<ReplayState>() != null)
            return [];
        var item = tooltip.CardInstance?.TryCast<ItemCard>();
        var template = tooltip.CardTemplate?.TryCast<TCardItem>();
        if (item == null || template?.Enchantments == null || !IsEligibleSection(item))
            return [];
        var current = AndroidGameIds.Unboxed(() => item.Enchantment);
        var result = new List<EEnchantmentType>();
        foreach (var entry in template.Enchantments)
            if (entry.Value != null && (!current.HasValue || entry.Key != current.Value))
                result.Add(entry.Key);
        return result.OrderBy(type => (int)type).Select(type => type.ToString()).ToArray();
    }

    public bool HoldsUpgrade(CardTooltipData tooltip) =>
        AndroidUpgradePreview.Holds(tooltip.CardInstance);

    public string? Effect(CardTooltipData tooltip, string enchantment) =>
        Enum.TryParse<EEnchantmentType>(enchantment, out var type)
            ? RenderEffect(tooltip, type, Data.Run?.TryCast<IRun>())
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
            var effect = RenderEffect(tooltip, type, Data.Run?.TryCast<IRun>(), tier);
            if (
                !string.IsNullOrWhiteSpace(effect)
                && AndroidCardFacets.Tier.TryToFacet(tier, out var facet)
            )
                effects.Add(new CollectionTierTooltipText(facet, effect));
        }
        return effects;
    }

    public string Label(string enchantment)
    {
        try
        {
            return new LocalizableText(enchantment).GetLocalizedText();
        }
        catch
        {
            return enchantment;
        }
    }

    // The desktop's ItemEnchantPreviewEligibility: an item on a board or in a stash, or one no
    // player owns yet, such as a shop's.
    private static bool IsEligibleSection(ItemCard item)
    {
        var section = AndroidGameIds.Unboxed(() => item.Section);
        return section is EInventorySection.Hand or EInventorySection.Stash || item.Owner == null;
    }

    // The game's lock-mode preview card: the item at its tier with this enchantment applied,
    // which carries the enchantment's attributes and lets its ability tokens resolve.
    internal static string? RenderEffect(
        CardTooltipData tooltipData,
        EEnchantmentType type,
        IRun? run,
        ETier? tier = null
    )
    {
        var card = tooltipData.CardInstance;
        var template = tooltipData.CardTemplate;
        var itemTemplate = template?.TryCast<TCardItem>();
        if (
            card == null
            || template == null
            || itemTemplate?.Enchantments == null
            || !itemTemplate.Enchantments.ContainsKey(type)
        )
            return null;
        var tooltips = itemTemplate.Enchantments[type]?.Localization?.Tooltips;
        if (tooltips == null || tooltips.Count == 0)
            return null;

        var preview = Il2CppTheBazaar.CardExtensions.CreateCopy(card);
        var previewItem = preview.TryCast<ItemCard>();
        if (previewItem == null)
            return null;
        preview.Tier = tier ?? card.Tier;
        var attributes = Il2CppTheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            template,
            preview.Tier
        );
        Il2CppTheBazaar.CardExtensions.ApplyPreviewEnchantment(
            itemTemplate,
            previewItem,
            attributes,
            new Il2CppSystem.Nullable<EEnchantmentType>(type)
        );
        preview.Attributes = attributes;
        var context = new TooltipContext(
            preview,
            template,
            new ValueContext(run!, preview.TryCast<ICard>())
        );
        var previewTooltip = new CardTooltipData(preview, template, null);

        var text = new StringBuilder();
        foreach (var tooltip in tooltips)
        {
            var content = tooltip?.Content;
            if (content == null)
                continue;
            var localized = TooltipExtensions.GetLocalizedText(content);
            if (string.IsNullOrWhiteSpace(localized))
                continue;
            var rendered = previewTooltip.RenderTooltip(TooltipBuilder.Create(context, localized));
            if (string.IsNullOrWhiteSpace(rendered))
                continue;
            if (text.Length > 0)
                text.Append('\n');
            text.Append(rendered.Trim());
        }
        return text.Length == 0 ? null : text.ToString();
    }
}
