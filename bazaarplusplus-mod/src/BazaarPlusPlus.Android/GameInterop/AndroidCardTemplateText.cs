using BazaarPlusPlus.Android.GameInterop.CardRental;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Cards.Interfaces;
using Il2CppBazaarGameShared.Domain.Cards.Item;
using Il2CppBazaarGameShared.Domain.Core;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar.Tooltips;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// A static card template's text in the current language, the one card-text resolver every
/// Android inspection (Collection, Ten-Win Builds, History) reads: its title (or internal name),
/// and its description as the game's own tooltip renders it for the card's tier and enchantment
/// (<see cref="Description(CardRentalSubject)"/>), its <c>{ability.*}</c> and <c>{aura.*}</c>
/// values resolved to numbers. When the game cannot render it, as before static data is ready,
/// the authored text stands in with its placeholders dropped. Null when the template is unknown
/// or the lookup fails. Main thread only.
/// </summary>
internal static class AndroidCardTemplateText
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
        var tier = AndroidCardFacets.Tier.TryToFacet(template.StartingTier, out var facet)
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
            || !AndroidCardFacets.Enchantment.TryToSource(enchantment, out var native)
        )
            return null;
        return Guarded(
            "enchantment",
            () =>
            {
                var tooltip = TooltipData(template, subject);
                // Static catalog previews must not inherit the last run's value context.
                return tooltip == null
                    ? null
                    : AndroidEnchantPreview.RenderEffect(tooltip, native, null);
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
            return AndroidGameIds.Unboxed<float>(() => tooltip.GetCurrentAttributeValue(attribute));
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
                    BppLogFeatureScope.Android,
                    "android.card_text.render_failed",
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
        return template?.TryCast<ITCard>() is { } cardTemplate
            ? Guarded("preview_card", () => PreviewCard(template, cardTemplate, subject))
            : null;
    }

    private static CardTooltipData? TooltipData(TCardBase template, CardRentalSubject subject)
    {
        var cardTemplate = template.TryCast<ITCard>();
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
        var tier = AndroidCardFacets.Tier.TryToSource(subject.Tier, out var nativeTier)
            ? nativeTier
            : template.StartingTier;
        var card = Il2CppTheBazaar.DTOUtils.CreateCard(template.Id.ToString(), template.Type);
        card.Attributes = new Il2CppSystem.Collections.Generic.Dictionary<
            ECardAttributeType,
            int
        >();
        // The template's tiers up to its starting tier, as the prefab's UpdateInstanceFromTierData.
        var tierData = template.TryCast<IHasTierData>();
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
        var attributes = Il2CppTheBazaar.CardExtensions.BuildAttributeDictionaryForTier(
            card,
            cardTemplate,
            tier
        );
        var item = card.TryCast<ItemCard>();
        var itemTemplate = template.TryCast<TCardItem>();
        if (item != null && itemTemplate != null)
            Il2CppTheBazaar.CardExtensions.ApplyPreviewEnchantment(
                itemTemplate,
                item,
                attributes,
                subject.Enchantment is { } enchantment
                && AndroidCardFacets.Enchantment.TryToSource(enchantment, out var native)
                    ? new Il2CppSystem.Nullable<EEnchantmentType>(native)
                    : new Il2CppSystem.Nullable<EEnchantmentType>()
            );
        card.Attributes = attributes;
        card.Size = template.Size;
        card.Tags = template.Tags;
        return card;
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
            if (!Il2CppTheBazaar.Data.IsManagerCreated())
                return null;
            return Il2CppTheBazaar
                .Data.GetStatic()
                ?.GetCardById(Il2CppSystem.Guid.Parse(templateId.ToString()))
                ?.TryCast<TCardBase>();
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
            var localized = Il2CppTheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(text);
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
