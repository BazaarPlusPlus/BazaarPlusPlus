#nullable enable
using System.Text.RegularExpressions;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Resolves the {ability.<id>} placeholders of a card's description or tooltip text for the
// Collection catalog. A host supplies the per-ability value resolver over its game templates; an
// unresolved placeholder drops, with the empty brackets it leaves behind.
public static class CollectionAbilityPlaceholders
{
    // Installed at host startup: maps a canonical attribute keyword ("Heal") to the game's
    // localized display word ("治疗" on zh clients). Null (or a null return) falls back to the
    // English name so the data layer never depends on game UI services directly.
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
