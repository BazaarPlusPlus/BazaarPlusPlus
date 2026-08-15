#nullable enable
using System.Text.RegularExpressions;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.GameInterop.Cards;

namespace BazaarPlusPlus.Game.EventPreview;

internal delegate bool LiveAbilityValueResolver(
    Guid templateId,
    string effectId,
    bool isAura,
    out string valueText,
    out string? unit
);

internal static class EventPreviewLocalization
{
    internal static Func<string, string?>? AttributeUnitLocalizer { get; set; }

    internal static string? ResolveTitle(EncounterPreviewTemplatePlan template) =>
        template == null ? null : PickText(template.Title);

    internal static string? ResolveDescription(
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

    internal static IReadOnlyDictionary<string, EncounterPreviewAbilityValue> CaptureAbilityValues(
        TCardBase template
    )
    {
        var result = new Dictionary<string, EncounterPreviewAbilityValue>(StringComparer.Ordinal);
        var abilities = template.Abilities;
        if (abilities != null)
        {
            foreach (var abilityId in abilities.Keys)
            {
                AddAbilityValue(abilityId);
                AddAbilityValue($"{abilityId}.mod");
            }
        }
        var auras = template.Auras;
        if (auras != null)
        {
            foreach (var auraId in auras.Keys)
            {
                AddAuraValue(auraId);
                AddAuraValue($"{auraId}.mod");
            }
        }
        return result;

        void AddAbilityValue(string placeholder)
        {
            if (CardAbilityValueReader.TryRead(template, placeholder, out var value))
                result[placeholder] = new EncounterPreviewAbilityValue(value.ValueText, value.Unit);
        }

        void AddAuraValue(string placeholder)
        {
            if (CardAbilityValueReader.TryReadAura(template, placeholder, out var value))
            {
                // Keep the existing plan/cache model; namespacing only aura keys avoids
                // colliding with legacy unprefixed ability ids.
                result[$"aura.{placeholder}"] = new EncounterPreviewAbilityValue(
                    value.ValueText,
                    value.Unit
                );
            }
        }
    }

    private static string? PickText(EncounterPreviewLocalizedText text)
    {
        var localizable = new TLocalizableText
        {
            Key = text.Key ?? string.Empty,
            Text = text.FallbackText ?? string.Empty,
        };
        try
        {
            var localized = TheBazaar.Tooltips.TooltipExtensions.GetLocalizedText(localizable);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }
        catch (Exception)
        {
            // Localization services are unavailable in tests and during early startup.
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
