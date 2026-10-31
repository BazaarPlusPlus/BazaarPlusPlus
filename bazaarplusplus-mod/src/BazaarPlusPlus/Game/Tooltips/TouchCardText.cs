#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Interfaces;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.ItemEnchantPreview.Preview;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

/// <summary>
/// A static card template's text in the current language, the one card-text resolver every
/// touch inspection reads (Collection on a mobile platform): its title (or internal name),
/// and its description as the game's own tooltip renders it for the card's tier and enchantment
/// (<see cref="Description(CardRentalSubject)"/>), its <c>{ability.*}</c> and <c>{aura.*}</c>
/// values resolved to numbers. When the game cannot render it, as before static data is ready,
/// the authored text stands in with its placeholders dropped. Null when the template is unknown
/// or the lookup fails. Main thread only.
/// </summary>
internal static class TouchCardText
{
    public static string? Title(Guid templateId)
    {
        var template = Template(templateId);
        if (template == null)
            return null;
        return Text(template.Localization?.Title)
            ?? (string.IsNullOrWhiteSpace(template.InternalName) ? null : template.InternalName);
    }

    public static string? Description(Guid templateId)
    {
        var template = Template(templateId);
        if (template == null)
            return null;
        var tier = DesktopCardFacets.Tier.TryToFacet(template.StartingTier, out var facet)
            ? facet
            : CardTier.Bronze;
        return Description(new CardRentalSubject(templateId, tier));
    }

    /// <summary>
    /// The card's description and its ability and aura lines as the game's tooltip shows them for
    /// <paramref name="subject"/>'s tier and enchantment: the card the game's preview prefab builds
    /// in its own set-up (<c>CardPreviewBase.SetUp</c>), rendered through its
    /// <see cref="CardTooltipData"/>. Falls back to <see cref="Authored"/> when that throws or
    /// renders nothing. Rich text: the game's colors and keyword sprites.
    /// </summary>
    public static string? Description(CardRentalSubject subject)
    {
        var template = Template(subject.TemplateId);
        if (template == null)
            return null;
        var tooltip = Guarded("tooltip_data", () => TooltipData(template, subject));
        return tooltip == null ? Authored(template) : DescriptionFrom(tooltip, template);
    }

    /// <summary>
    /// What an inspection shows for <paramref name="subject"/>'s tier and enchantment from one
    /// template lookup and one <see cref="CardTooltipData"/>: the text
    /// <see cref="Description(CardRentalSubject)"/> returns, and the out-of-combat cooldown and
    /// ammo the tooltip reads. Values are null when the tooltip cannot be built or lacks them.
    /// </summary>
    public static (string? Text, float? Cooldown, float? Ammo) Preview(CardRentalSubject subject)
    {
        var template = Template(subject.TemplateId);
        if (template == null)
            return (null, null, null);
        var tooltip = Guarded("tooltip_data", () => TooltipData(template, subject));
        if (tooltip == null)
            return (Authored(template), null, null);
        return (
            DescriptionFrom(tooltip, template),
            ReadAttribute(tooltip, ECardAttributeType.CooldownMax),
            ReadAttribute(tooltip, ECardAttributeType.AmmoMax)
        );
    }

    /// <summary>
    /// The same enchantment-only effect renderer used by in-game tooltip previews, for a
    /// <paramref name="template"/> the caller looked up once (<see cref="Template"/>) across its
    /// enchantments and tiers. Null when the template is unknown or the render fails.
    /// </summary>
    public static string? EnchantmentEffect(
        TCardBase? template,
        CardRentalSubject subject,
        CardEnchantment enchantment
    )
    {
        if (
            template == null
            || !DesktopCardFacets.Enchantment.TryToSource(enchantment, out var native)
        )
            return null;
        return Guarded(
            "enchantment",
            () =>
            {
                var tooltip = TooltipData(template, subject);
                // Static catalog previews must not inherit the last run's value context.
                return tooltip == null ? null : RenderEffect(tooltip, native);
            }
        );
    }

    // Each block on its own: an effect token that needs the run, absent at the main menu, costs
    // only its block. The authored text stands in when neither renders.
    private static string? DescriptionFrom(CardTooltipData tooltip, TCardBase template)
    {
        var lines = new List<string>();
        Add(lines, Guarded("description", () => tooltip.GetCardDescription().Item1));
        Add(lines, Guarded("effects", () => tooltip.BuildCombinedEffectDescription()));
        return lines.Count == 0 ? Authored(template) : string.Join("\n", lines);
    }

    private static float? ReadAttribute(CardTooltipData tooltip, ECardAttributeType attribute)
    {
        try
        {
            return tooltip.GetCurrentAttributeValue(attribute);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static T? Guarded<T>(string step, Func<T?> render)
        where T : class
    {
        try
        {
            return render();
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Touch,
                    "touch.card_text.render_failed",
                    storm: ["step"]
                ),
                exception,
                ("step", step)
            );
            return null;
        }
    }

    /// <summary>
    /// A detached card for <paramref name="subject"/>'s template, tier and enchantment, built as
    /// the game's preview prefab builds it; null when the template is unknown or the build fails.
    /// </summary>
    public static Card? PreviewCard(CardRentalSubject subject)
    {
        var template = Template(subject.TemplateId);
        return template as ITCard is { } cardTemplate
            ? Guarded("preview_card", () => PreviewCard(template, cardTemplate, subject))
            : null;
    }

    private static CardTooltipData? TooltipData(TCardBase template, CardRentalSubject subject)
    {
        var cardTemplate = (template as ITCard);
        if (cardTemplate == null)
            return null;
        return new CardTooltipData(
            PreviewCard(template, cardTemplate, subject),
            cardTemplate,
            null
        );
    }

    private static Card PreviewCard(
        TCardBase template,
        ITCard cardTemplate,
        CardRentalSubject subject
    )
    {
        var tier = DesktopCardFacets.Tier.TryToSource(subject.Tier, out var nativeTier)
            ? nativeTier
            : template.StartingTier;
        var card = TheBazaar.DTOUtils.CreateCard(template.Id.ToString(), template.Type);
        card.Attributes = new System.Collections.Generic.Dictionary<ECardAttributeType, int>();
        // The template's tiers up to its starting tier, as the prefab's UpdateInstanceFromTierData.
        var tierData = (template as IHasTierData);
        if (tierData?.Tiers != null)
            foreach (var entry in tierData.Tiers)
            {
                if (entry.Key > template.StartingTier)
                    continue;
                if (entry.Value?.Attributes == null)
                    continue;
                foreach (var attribute in entry.Value.Attributes)
                    card.Attributes[attribute.Key] = attribute.Value;
            }
        card.Tier = tier;
        var attributes = TheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            cardTemplate,
            tier
        );
        var item = (card as ItemCard);
        var itemTemplate = (template as TCardItem);
        if (item != null && itemTemplate != null)
            TheBazaar.CardExtensions.ApplyPreviewEnchantment(
                itemTemplate,
                item,
                attributes,
                subject.Enchantment is { } enchantment
                && DesktopCardFacets.Enchantment.TryToSource(enchantment, out var native)
                    ? new System.Nullable<EEnchantmentType>(native)
                    : new System.Nullable<EEnchantmentType>()
            );
        card.Attributes = attributes;
        card.Size = template.Size;
        card.Tags = template.Tags;
        return card;
    }

    // The game's lock-mode preview card: the item at its tier with this enchantment applied,
    // which carries the enchantment's attributes and lets its ability tokens resolve. A static
    // catalog preview reads no run, as on Android.
    internal static string? RenderEffect(
        CardTooltipData tooltipData,
        EEnchantmentType type,
        ETier? tier = null
    )
    {
        var card = tooltipData.CardInstance;
        var template = tooltipData.CardTemplate;
        if (
            card == null
            || template is not TCardItem itemTemplate
            || itemTemplate.Enchantments == null
            || !itemTemplate.Enchantments.TryGetValue(type, out var enchantment)
        )
            return null;
        var tooltips = enchantment?.Localization?.Tooltips;
        if (tooltips == null || tooltips.Count == 0)
            return null;
        if (TheBazaar.CardExtensions.CreateCopy(card) is not ItemCard preview)
            return null;
        preview.Template ??= template;
        preview.Tier = tier ?? card.Tier;
        var attributes = TheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            template,
            preview.Tier
        );
        TheBazaar.CardExtensions.ApplyPreviewEnchantment(itemTemplate, preview, attributes, type);
        preview.Attributes = attributes;
        var text = new System.Text.StringBuilder();
        // The raw rendered effect: the desktop's segments add their own label and inline sizes,
        // which the touch list draws itself.
        foreach (var tooltip in tooltips)
        {
            if (tooltip?.Content is not { } content)
                continue;
            var rendered = ItemEnchantPreviewRenderer.RenderTooltipText(preview, content).Trim();
            if (string.IsNullOrWhiteSpace(rendered))
                continue;
            if (text.Length > 0)
                text.Append('\n');
            text.Append(rendered);
        }
        return text.Length == 0 ? null : text.ToString();
    }

    private static void Add(List<string> lines, string? text)
    {
        var trimmed = text?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && !lines.Contains(trimmed))
            lines.Add(trimmed);
    }

    // The description, or else the tooltips one per line, as most items author their text there.
    private static string? Authored(TCardBase template)
    {
        var localization = template.Localization;
        if (localization == null)
            return null;
        var description = Formatted(Text(localization.Description));
        if (description != null)
            return description;
        var lines = new List<string>();
        try
        {
            var tooltips = localization.Tooltips;
            if (tooltips != null)
                foreach (var tooltip in tooltips)
                    if (Formatted(Text(tooltip?.Content)) is { } line && !lines.Contains(line))
                        lines.Add(line);
        }
        catch (Exception)
        {
            // A partial list is still worth showing.
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string? Formatted(string? text)
    {
        var formatted = CollectionAbilityPlaceholders.Format(text, NoAbilityValue)?.Trim();
        if (formatted != null)
            formatted = CollectionAbilityPlaceholders.DropAuraPlaceholders(formatted).Trim();
        return string.IsNullOrWhiteSpace(formatted) ? null : formatted;
    }

    /// <summary>The static template for <paramref name="templateId"/>; null when unknown or not ready.</summary>
    public static TCardBase? Template(Guid templateId)
    {
        try
        {
            return TouchTemplates.Find(templateId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Text(TLocalizableText? text)
    {
        if (text == null)
            return null;
        try
        {
            var localized = TheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(text);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }
        catch (Exception)
        {
            // The localization service may not be up; the authored English stands in.
        }
        return string.IsNullOrWhiteSpace(text.Text) ? null : text.Text;
    }

    private static bool NoAbilityValue(string abilityId, out string valueText, out string? unit)
    {
        valueText = string.Empty;
        unit = null;
        return false;
    }
}
