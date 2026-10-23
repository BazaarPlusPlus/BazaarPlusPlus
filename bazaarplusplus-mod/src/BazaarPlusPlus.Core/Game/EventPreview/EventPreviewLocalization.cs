#nullable enable
using System.Text.RegularExpressions;

namespace BazaarPlusPlus.Game.EventPreview;

public delegate bool LiveAbilityValueResolver(
    Guid templateId,
    string effectId,
    bool isAura,
    out string valueText,
    out string? unit
);

public static class EventPreviewLocalization
{
    public static Func<string, string?>? AttributeUnitLocalizer { get; set; }

    // Host port: the game's localization of a template's title or description key. Unset (in
    // tests and before the host installs it) or failing, a text falls back to its English text,
    // then its key.
    public static Func<EncounterPreviewLocalizedText, string?>? TextLocalizer { get; set; }

    public static string? ResolveTitle(EncounterPreviewTemplatePlan template) =>
        template == null ? null : PickText(template.Title);

    public static string? ResolveDescription(
        EncounterPreviewTemplatePlan template,
        LiveAbilityValueResolver? resolveLiveValue = null
    )
    {
        if (template == null)
            return null;
        return FormatAbilityPlaceholders(
            template,
            PickText(template.Description),
            resolveLiveValue
        );
    }

    private static string? PickText(EncounterPreviewLocalizedText text)
    {
        try
        {
            var localized = TextLocalizer?.Invoke(text);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }
        catch (Exception)
        {
            // The game's localization services are unavailable during early startup.
        }

        if (!string.IsNullOrWhiteSpace(text.FallbackText))
            return text.FallbackText;
        return string.IsNullOrWhiteSpace(text.Key) ? null : text.Key;
    }

    private static string? FormatAbilityPlaceholders(
        EncounterPreviewTemplatePlan template,
        string? text,
        LiveAbilityValueResolver? resolveLiveValue
    ) =>
        FormatAbilityPlaceholders(
            text,
            (string abilityId, out string valueText, out string? unit) =>
            {
                if (template.AbilityValues.TryGetValue(abilityId, out var value))
                {
                    valueText = value.ValueText;
                    unit = value.Unit;
                    return true;
                }

                const string AuraPrefix = "aura.";
                var isAura = abilityId.StartsWith(AuraPrefix, StringComparison.Ordinal);
                var effectId = isAura ? abilityId[AuraPrefix.Length..] : abilityId;
                if (
                    resolveLiveValue != null
                    && resolveLiveValue(
                        template.TemplateId,
                        effectId,
                        isAura,
                        out valueText,
                        out unit
                    )
                )
                    return true;

                valueText = string.Empty;
                unit = null;
                return false;
            }
        );

    private delegate bool AbilityValueResolver(
        string abilityId,
        out string valueText,
        out string? unit
    );

    private static string? FormatAbilityPlaceholders(
        string? text,
        AbilityValueResolver resolveAbilityValue
    )
    {
        if (
            string.IsNullOrWhiteSpace(text)
            || (
                !text.Contains("{ability.", StringComparison.Ordinal)
                && !text.Contains("{aura.", StringComparison.Ordinal)
            )
        )
            return text;

        var formatted = Regex.Replace(
            text!,
            @"\{(ability|aura)\.([^}]+)\}",
            match =>
            {
                var placeholder =
                    match.Groups[1].Value == "ability"
                        ? match.Groups[2].Value
                        : $"aura.{match.Groups[2].Value}";
                if (!resolveAbilityValue(placeholder, out var value, out var unit))
                    return string.Empty;
                if (unit == null)
                    return value;

                var localizedUnit = AttributeUnitLocalizer?.Invoke(unit);
                if (string.IsNullOrWhiteSpace(localizedUnit))
                    localizedUnit = unit;

                var after = match.Index + match.Length;
                var next = after;
                while (next < text!.Length && char.IsWhiteSpace(text[next]))
                    next++;
                if (next < text.Length && IsCjk(text[next]))
                    return value;

                if (
                    FollowingWordEquals(text!, after, unit)
                    || FollowingWordEquals(text!, after, localizedUnit!)
                )
                    return value;
                if (UnitAliases.TryGetValue(unit, out var aliases))
                    foreach (var alias in aliases)
                        if (FollowingWordEquals(text!, after, alias))
                            return value;

                return IsCjk(localizedUnit![0])
                    ? $"{value}{localizedUnit}"
                    : $"{value} {localizedUnit}";
            },
            RegexOptions.CultureInvariant
        );
        return CleanDroppedPlaceholders(formatted);
    }

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
        return IsCjk(word[0])
            || index + word.Length == text.Length
            || !char.IsLetter(text[index + word.Length]);
    }

    private static bool IsCjk(char value) => value >= '⺀';

    private static readonly Dictionary<string, string[]> UnitAliases = new(StringComparer.Ordinal)
    {
        ["Experience"] = new[] { "XP" },
    };
}
