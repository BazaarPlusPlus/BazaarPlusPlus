#nullable enable
using System.Text.RegularExpressions;
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.EventPreview;

public static class EncounterRewardParser
{
    private static readonly (string Phrase, string EnumName)[] ItemTagTerms =
    {
        ("weapon", "Weapon"),
        ("friend", "Friend"),
        ("aquatic", "Aquatic"),
        ("tool", "Tool"),
        ("drone", "Drone"),
        ("vehicle", "Vehicle"),
        ("food", "Food"),
        ("trap", "Trap"),
        ("toy", "Toy"),
        ("potion", "Potion"),
        ("reagent", "Reagent"),
        ("relic", "Relic"),
        ("dragon", "Dragon"),
        ("core", "Core"),
        ("tech", "Tech"),
        ("dinosaur", "Dinosaur"),
        ("ray", "Ray"),
        ("apparel", "Apparel"),
        ("property", "Property"),
        ("loot", "Loot"),
    };

    private static readonly (string Phrase, string EnumName)[] KeywordTerms =
    {
        ("flying", "Flying"),
        ("haste", "Haste"),
        ("charge", "Charge"),
        ("cooldown", "Cooldown"),
        ("slow", "Slow"),
        ("freeze", "Freeze"),
        ("damage", "Damage"),
        ("shield", "Shield"),
        ("heal", "Heal"),
        ("health", "Health"),
        ("burn", "Burn"),
        ("poison", "Poison"),
        ("regen", "Regen"),
        ("crit", "Crit"),
        ("ammo", "Ammo"),
        ("lifesteal", "Lifesteal"),
        ("rage", "Rage"),
        ("gold", "Gold"),
        ("income", "Income"),
        ("value", "Value"),
    };

    public static EncounterRewardFilter? TryParse(string? resultText)
    {
        if (string.IsNullOrWhiteSpace(resultText))
            return null;

        var lower = resultText!.ToLowerInvariant();
        var isSkillReward = ContainsWord(lower, "skill") || ContainsWord(lower, "skills");
        var tags = ParseEnums<CardTag>(lower, ItemTagTerms);
        var isItemReward =
            ContainsWord(lower, "item") || ContainsWord(lower, "items") || tags.Count > 0;

        if (!isSkillReward && !isItemReward)
            return null;

        var cardType = isSkillReward ? CardType.Skill : CardType.Item;
        var sizes = cardType == CardType.Item ? ParseSizes(lower) : Array.Empty<CardSize>();
        var tiers = ParseTiers(lower);
        var keywords = ParseEnums<CardHiddenTag>(lower, KeywordTerms);
        if (cardType == CardType.Skill)
            tags = Array.Empty<CardTag>();

        var summary = BuildSummary(cardType, sizes, tiers, tags, keywords);
        return new EncounterRewardFilter(
            cardType,
            ParseQuantity(lower, cardType),
            lower.Contains("from any hero", StringComparison.Ordinal),
            sizes,
            tiers,
            tags,
            keywords,
            summary
        );
    }

    private static int? ParseQuantity(string lower, CardType cardType)
    {
        var match = Regex.Match(
            lower,
            @"\b(?:get|learn|gain)\s+(?:(?<count>\d+)|a|an)\b",
            RegexOptions.CultureInvariant
        );
        if (match.Success && int.TryParse(match.Groups["count"].Value, out var count))
            return count;

        return cardType == CardType.Item || cardType == CardType.Skill ? 1 : null;
    }

    private static IReadOnlyList<CardSize> ParseSizes(string lower)
    {
        var result = new List<CardSize>(3);
        AddIfMentioned(lower, "small", CardSize.Small, result);
        AddIfMentioned(lower, "medium", CardSize.Medium, result);
        AddIfMentioned(lower, "large", CardSize.Large, result);
        return result;
    }

    private static IReadOnlyList<CardTier> ParseTiers(string lower)
    {
        var result = new List<CardTier>(5);
        AddIfMentioned(lower, "bronze", CardTier.Bronze, result);
        AddIfMentioned(lower, "silver", CardTier.Silver, result);
        AddIfMentioned(lower, "gold", CardTier.Gold, result);
        AddIfMentioned(lower, "diamond", CardTier.Diamond, result);
        AddIfMentioned(lower, "legendary", CardTier.Legendary, result);
        return result;
    }

    private static IReadOnlyList<TEnum> ParseEnums<TEnum>(
        string lower,
        IReadOnlyList<(string Phrase, string EnumName)> terms
    )
        where TEnum : struct
    {
        var result = new List<TEnum>();
        foreach (var term in terms)
        {
            if (!ContainsWord(lower, term.Phrase))
                continue;
            if (!Enum.TryParse<TEnum>(term.EnumName, ignoreCase: true, out var value))
                continue;
            if (!result.Contains(value))
                result.Add(value);
        }
        return result;
    }

    private static void AddIfMentioned<TEnum>(
        string lower,
        string word,
        TEnum value,
        List<TEnum> result
    )
        where TEnum : struct
    {
        if (!ContainsWord(lower, word))
            return;
        if (!result.Contains(value))
            result.Add(value);
    }

    private static string BuildSummary(
        CardType cardType,
        IReadOnlyList<CardSize> sizes,
        IReadOnlyList<CardTier> tiers,
        IReadOnlyList<CardTag> tags,
        IReadOnlyList<CardHiddenTag> keywords
    )
    {
        var parts = new List<string>();
        foreach (var size in sizes)
            parts.Add(size.ToString());
        foreach (var tier in tiers)
            parts.Add(tier.ToString());
        foreach (var tag in tags)
            parts.Add(tag.ToString());
        foreach (var keyword in keywords)
            parts.Add(keyword.ToString());

        if (cardType == CardType.Skill)
            parts.Add("skill");

        return parts.Count == 0
            ? (cardType == CardType.Skill ? "Skill" : "Item")
            : string.Join(" ", parts);
    }

    private static bool ContainsWord(string lower, string word) =>
        Regex.IsMatch(
            lower,
            $@"(^|[^a-z]){Regex.Escape(word)}s?([^a-z]|$)",
            RegexOptions.CultureInvariant
        );
}
