using System.Text;
using BazaarPlusPlus.Game.CollectionPanel.Tooltips;
using BazaarPlusPlus.Game.ItemEnchantPreview;
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

/// <summary>Native enchantment effects shared by the live tooltip drawer and Collection.</summary>
internal static class AndroidEnchantPreview
{
    public static long Rendered { get; private set; }

    public static IReadOnlyList<EEnchantmentType> Candidates(CardTooltipData tooltip)
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
                var effect = RenderEffect(tooltip, type, Data.Run?.TryCast<IRun>(), tier);
                if (
                    !string.IsNullOrWhiteSpace(effect)
                    && AndroidCardFacets.Tier.TryToFacet(tier, out var facet)
                )
                    effects.Add(new CollectionTierTooltipText(facet, effect));
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
        Rendered++;
        return lines;
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

    private static string Label(EEnchantmentType type)
    {
        var name = type.ToString();
        try
        {
            return new LocalizableText(name).GetLocalizedText();
        }
        catch
        {
            return name;
        }
    }
}
