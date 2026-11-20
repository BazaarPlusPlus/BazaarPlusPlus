#nullable enable
using System.Text.RegularExpressions;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public static class CollectionAbilityPlaceholders
{
    // A missing localizer falls back to English so the catalog can build without the game's UI services.
    public static Func<string, string?>? AttributeUnitLocalizer { get; set; }

    public delegate bool AbilityValueResolver(
        string abilityId,
        out string valueText,
        out string? unit
    );

    public static string? Format(string? text, AbilityValueResolver resolveAbilityValue)
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
                if (!resolveAbilityValue(match.Groups[1].Value, out var value, out var unit))
                    return string.Empty;
                if (unit == null)
                    return value;

                var localizedUnit = AttributeUnitLocalizer?.Invoke(unit);
                if (string.IsNullOrWhiteSpace(localizedUnit))
                    localizedUnit = unit;

                var after = match.Index + match.Length;

                // CJK templates put their units or measure words after the placeholder, so adding a unit can duplicate them.
                var next = after;
                while (next < text!.Length && char.IsWhiteSpace(text[next]))
                    next++;
                if (next < text.Length && IsCjk(text[next]))
                    return value;

                // Templates can spell out a unit already, sometimes under an alias such as XP.
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

    /// <summary>Hosts without the game's tooltip renderer have no aura values to substitute.</summary>
    public static string DropAuraPlaceholders(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{aura.", StringComparison.Ordinal))
            return text;
        return CleanDroppedPlaceholders(
            Regex.Replace(text, @"\{aura\.[^}]+\}", string.Empty, RegexOptions.CultureInvariant)
        );
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
