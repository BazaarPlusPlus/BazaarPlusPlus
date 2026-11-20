using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.CardRental;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Cards;
using Il2CppBazaarGameShared.Domain.Core;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar.Tooltips;

namespace BazaarPlusPlus.Android.GameInterop;

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

    public static string? Description(CardRentalSubject subject)
    {
        var template = Template(subject.TemplateId);
        if (template == null)
            return null;
        var tooltip = Guarded("tooltip_data", () => TooltipData(template, subject));
        return tooltip == null ? Authored(template) : DescriptionFrom(tooltip, template);
    }

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
                    : AndroidEnchantPreviewHost.RenderEffect(tooltip, native, null);
            }
        );
    }

    // Some tokens require live run data, so one block can fail at the main menu while the other
    // still renders.
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
        var card = Il2CppTheBazaar.DTOUtils.CreateCard(template.Id.ToString(), template.Type);
        AndroidNativeCardSetUp.Fill(card, template, cardTemplate, subject);
        return card;
    }

    private static void Add(List<string> lines, string? text)
    {
        var trimmed = text?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && !lines.Contains(trimmed))
            lines.Add(trimmed);
    }

    // Most items store their authored description in the tooltip lines.
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
        catch (Exception) { }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string? Formatted(string? text)
    {
        var formatted = CollectionAbilityPlaceholders.Format(text, NoAbilityValue)?.Trim();
        if (formatted != null)
            formatted = CollectionAbilityPlaceholders.DropAuraPlaceholders(formatted).Trim();
        return string.IsNullOrWhiteSpace(formatted) ? null : formatted;
    }

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
