#nullable enable
using System.Text.RegularExpressions;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// TCardLocalization carries Title/Description TLocalizableText with Key + Text. The Text
// field is the authored (English) fallback; the current-language translation resolves via
// TooltipExtensions.GetLocalizedText -> LocalizationService.TryGetText keyed lookup, which
// is what the native tooltip pipeline uses. We do the same, falling back to Text (and then
// the key for diagnostic visibility) when the service is unavailable — e.g. in unit tests.
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
            // Localization service unavailable (unit tests / early startup):
            // fall back to the authored text below.
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

    // Installed at plugin startup (Plugin.InstallStaticUtilities): maps a canonical attribute keyword
    // ("Heal") to the game's localized display word ("治疗" on zh clients) via
    // TooltipTypography. Null (or a null return) falls back to the English name so
    // the data layer never depends on game UI services directly.
    internal static Func<string, string?>? AttributeUnitLocalizer = null;

    private delegate bool AbilityValueResolver(
        string abilityId,
        out string valueText,
        out string? unit
    );

    private static string? FormatAbilityPlaceholders(TCardBase template, string? text) =>
        FormatAbilityPlaceholders(
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

    private static string? FormatAbilityPlaceholders(
        string? text,
        AbilityValueResolver resolveAbilityValue
    )
    {
        if (
            string.IsNullOrWhiteSpace(text) || !text.Contains("{ability.", StringComparison.Ordinal)
        )
            return text;

        var formatted = Regex.Replace(
            text!,
            @"\{ability\.([^}]+)\}",
            match =>
            {
                // Unresolvable placeholders (e.g. live-computed totals) degrade to
                // nothing rather than leaking raw tokens; leftover empty brackets
                // are cleaned afterwards.
                if (!resolveAbilityValue(match.Groups[1].Value, out var value, out var unit))
                    return string.Empty;
                if (unit == null)
                    return value;

                var localizedUnit = AttributeUnitLocalizer?.Invoke(unit);
                if (string.IsNullOrWhiteSpace(localizedUnit))
                    localizedUnit = unit;

                var after = match.Index + match.Length;

                // CJK text carries its own unit or measure word right after the
                // placeholder ("获得{ability.0}点经验值" / "…{ability.0}金币"),
                // so appending anything there only produces mixed-language noise.
                var next = after;
                while (next < text!.Length && char.IsWhiteSpace(text[next]))
                    next++;
                if (next < text.Length && IsCjk(text[next]))
                    return value;

                // Some templates spell the unit out right after the placeholder
                // ("Gain {ability.0} Gold" / "Gain {ability.0} XP" for Experience);
                // only append when the text does not already carry it, in either
                // language or a known alias.
                if (
                    FollowingWordEquals(text!, after, unit)
                    || FollowingWordEquals(text!, after, localizedUnit!)
                )
                    return value;
                if (UnitAliases.TryGetValue(unit, out var aliases))
                    foreach (var alias in aliases)
                        if (FollowingWordEquals(text!, after, alias))
                            return value;

                // CJK words join without a space.
                return IsCjk(localizedUnit![0])
                    ? $"{value}{localizedUnit}"
                    : $"{value} {localizedUnit}";
            },
            RegexOptions.CultureInvariant
        );
        return CleanDroppedPlaceholders(formatted);
    }

    // After dropping unresolvable placeholders, remove the empty bracket pairs and
    // doubled spaces they leave behind ("... you have [{ability.0}]" -> "... you have").
    private static string CleanDroppedPlaceholders(string text)
    {
        text = text.Replace("[]", string.Empty)
            .Replace("[ ]", string.Empty)
            .Replace("()", string.Empty)
            .Replace("（）", string.Empty);
        var builder = new System.Text.StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var character in text)
        {
            var isSpace = character == ' ';
            if (isSpace && previousWasSpace)
                continue;
            previousWasSpace = isSpace;
            builder.Append(character);
        }
        return builder.ToString().TrimEnd();
    }

    private static bool FollowingWordEquals(string text, int index, string word)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        if (index + word.Length > text.Length)
            return false;
        if (
            string.Compare(text, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase)
            != 0
        )
            return false;
        // CJK has no word boundaries; for Latin words require the match to end the word.
        if (IsCjk(word[0]))
            return true;
        return index + word.Length == text.Length || !char.IsLetter(text[index + word.Length]);
    }

    private static bool IsCjk(char value) => value >= '⺀';

    private static readonly Dictionary<string, string[]> UnitAliases = new(StringComparer.Ordinal)
    {
        ["Experience"] = new[] { "XP" },
    };
}
