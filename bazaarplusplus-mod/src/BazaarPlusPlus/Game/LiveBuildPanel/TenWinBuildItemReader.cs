#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.GameInterop.TagTypography;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

// Main thread only. The reader uses native card and tooltip data before the panel's board previews finish loading.
internal static class TenWinBuildItemReader
{
    public static TenWinBuildItemText Read(BppItemBoardCard saved)
    {
        var template = BppStaticDataAccess.GetCardTemplate(
            BppStaticDataAccess.TryGetReadyManagerObject(),
            saved.TemplateId
        );
        if (template is not ITCard cardTemplate)
            return new TenWinBuildItemText(
                saved.TemplateId.ToString(),
                null,
                Array.Empty<string>(),
                null,
                null,
                string.Empty
            );

        var card = Guarded("card", () => DetachedCard(template, cardTemplate, saved));
        var tooltip =
            card == null ? null : Guarded("tooltip", () => new CardTooltipData(card, cardTemplate));
        return new TenWinBuildItemText(
            Guarded("title", () => Text(template.Localization?.Title)) ?? template.InternalName,
            Guarded("enchantment", () => Enchantment(saved)),
            (card == null ? null : Guarded("tags", () => Tags(card))) ?? Array.Empty<string>(),
            tooltip?.GetCurrentAttributeValue(ECardAttributeType.CooldownMax),
            tooltip?.GetCurrentAttributeValue(ECardAttributeType.AmmoMax),
            tooltip == null ? string.Empty : Description(tooltip)
        );
    }

    private static Card DetachedCard(
        TCardBase template,
        ITCard cardTemplate,
        BppItemBoardCard saved
    )
    {
        var card = DTOUtils.CreateCard(template.Id.ToString(), template.Type);
        card.Attributes = new Dictionary<ECardAttributeType, int>();
        if (template is IHasTierData { Tiers: not null } tiered)
            foreach (var entry in tiered.Tiers)
            {
                if (entry.Key > template.StartingTier)
                    break;
                foreach (var attribute in entry.Value.Attributes)
                    card.Attributes[attribute.Key] = attribute.Value;
            }
        var tier = DesktopCardFacets.Tier.TryToSource(saved.Tier, out var nativeTier)
            ? nativeTier
            : template.StartingTier;
        card.Tier = tier;
        var attributes = TheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            cardTemplate,
            tier
        );
        if (card is ItemCard item && template is TCardItem itemTemplate)
            TheBazaar.CardExtensions.ApplyPreviewEnchantment(
                itemTemplate,
                item,
                attributes,
                saved.EnchantmentType is { } enchantment
                && DesktopCardFacets.Enchantment.TryToSource(enchantment, out var native)
                    ? native
                    : null
            );
        foreach (var pair in NativeCardSetUp.Values(saved.Attributes))
            attributes[pair.Key] = pair.Value;
        var size = DesktopCardFacets.Size.TryToSource(saved.Size, out var savedSize)
            ? savedSize
            : template.Size;
        if (template.Type == ECardType.Item && size < template.Size)
            attributes[ECardAttributeType.Tiny] = 1;
        card.Attributes = attributes;
        card.Size = size;
        card.Tags = template.Tags;
        return card;
    }

    private static string Description(CardTooltipData tooltip)
    {
        var lines = new List<string>();
        Add(lines, Guarded("description", () => tooltip.GetCardDescription().Item1));
        Add(lines, Guarded("effects", () => tooltip.BuildCombinedEffectDescription()));
        return string.Join("\n", lines);
    }

    private static string? Enchantment(BppItemBoardCard saved) =>
        saved.EnchantmentType is { } enchantment
        && DesktopCardFacets.Enchantment.TryToSource(enchantment, out var native)
            ? new LocalizableText(native.ToString()).GetLocalizedText()
            : null;

    private static IReadOnlyList<string> Tags(Card card) =>
        (DesktopEffectiveCardTags.Read(card).Tags ?? Array.Empty<CardTag>())
            .Select(tag => NativeTagTypography.Resolve(tag).Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal)
            .ToArray();

    private static string? Text(TLocalizableText? text)
    {
        var localized = text?.GetLocalizedText();
        return string.IsNullOrWhiteSpace(localized) ? null : localized;
    }

    private static void Add(List<string> lines, string? text)
    {
        var trimmed = text?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && !lines.Contains(trimmed!))
            lines.Add(trimmed!);
    }

    private static T? Guarded<T>(string part, Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.LiveBuildPanel,
                    "live_build_panel.copy.item_text_failed",
                    storm: ["part"]
                ),
                exception,
                ("part", part)
            );
            return null;
        }
    }
}
