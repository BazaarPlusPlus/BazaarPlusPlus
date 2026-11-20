#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// TLocalizableText.Text is the authored fallback. The native tooltip resolves the translation by key.
internal static partial class CollectionLocalizationResolver
{
    public static string? ResolveTitle(TCardBase template)
    {
        var title = template.Localization?.Title;
        if (title == null)
            return null;
        return PickText(title);
    }

    public static string? ResolveDescription(TCardBase template)
    {
        var description = template.Localization?.Description;
        if (description == null)
            return null;
        var text = PickText(description);
        return FormatAbilityPlaceholders(template, text);
    }

    public static IReadOnlyList<string> ResolveTitleSearchTexts(TCardBase template) =>
        SearchTexts(template, template.Localization?.Title, formatAbilityPlaceholders: false);

    public static IReadOnlyList<string> ResolveDescriptionSearchTexts(TCardBase template) =>
        SearchTexts(template, template.Localization?.Description, formatAbilityPlaceholders: true);

    public static IReadOnlyList<string> ResolveTooltipSearchTexts(TCardBase template)
    {
        var tooltips = template.Localization?.Tooltips;
        if (tooltips == null || tooltips.Count == 0)
            return Array.Empty<string>();

        var values = new List<string>(tooltips.Count * 3);
        foreach (var tooltip in tooltips)
            AddSearchTexts(values, template, tooltip?.Content, formatAbilityPlaceholders: true);
        return values;
    }

    private static IReadOnlyList<string> SearchTexts(
        TCardBase template,
        TLocalizableText? text,
        bool formatAbilityPlaceholders
    )
    {
        if (text == null)
            return Array.Empty<string>();

        var values = new List<string>(3);
        AddSearchTexts(values, template, text, formatAbilityPlaceholders);
        return values;
    }

    private static void AddSearchTexts(
        List<string> values,
        TCardBase template,
        TLocalizableText? text,
        bool formatAbilityPlaceholders
    )
    {
        if (text == null)
            return;

        AddSearchText(values, TryGetLocalizedText(text), template, formatAbilityPlaceholders);
        AddSearchText(values, text.Text, template, formatAbilityPlaceholders);
    }

    private static string? PickText(TLocalizableText text)
    {
        var localized = TryGetLocalizedText(text);
        if (!string.IsNullOrWhiteSpace(localized))
            return localized;

        if (!string.IsNullOrWhiteSpace(text.Text))
            return text.Text;
        if (!string.IsNullOrWhiteSpace(text.Key))
            return text.Key;
        return null;
    }

    private static string? TryGetLocalizedText(TLocalizableText text)
    {
        try
        {
            return TheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void AddSearchText(
        List<string> values,
        string? text,
        TCardBase template,
        bool formatAbilityPlaceholders
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var value = formatAbilityPlaceholders ? FormatAbilityPlaceholders(template, text) : text;
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var existing in values)
            if (string.Equals(existing, value, StringComparison.Ordinal))
                return;

        values.Add(value!);
    }

    private static string? FormatAbilityPlaceholders(TCardBase template, string? text) =>
        CollectionAbilityPlaceholders.Format(
            text,
            (string abilityId, out string valueText, out string? unit) =>
            {
                if (CardAbilityValueReader.TryRead(template, abilityId, out var value))
                {
                    valueText = value.ValueText;
                    unit = value.Unit;
                    return true;
                }

                valueText = string.Empty;
                unit = null;
                return false;
            }
        );
}
