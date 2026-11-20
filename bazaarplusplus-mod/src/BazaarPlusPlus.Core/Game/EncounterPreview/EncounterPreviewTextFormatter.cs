#nullable enable
using System.Globalization;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.EncounterPreview;

public static class EncounterPreviewTextFormatter
{
    private const string AccentColor = "#FFD37E";
    private const string IneligibleColor = "#8F8268";

    private static readonly CardTier[] DealableTiers =
    {
        CardTier.Bronze,
        CardTier.Silver,
        CardTier.Gold,
        CardTier.Diamond,
    };

    public static string Build(
        EncounterOption option,
        Func<string, string>? colorizeResult = null,
        GameDataDayTierTable? dayTiers = null
    )
    {
        if (option == null)
            return string.Empty;

        var rawColorize = colorizeResult ?? (text => text);
        string Colorize(string text) => TooltipMarkup.NormalizeInlineFragment(rawColorize(text));

        if (option.HasOutcomeGroups)
            return BuildOutcomes(option.OutcomeGroups!, Colorize, dayTiers);
        if (!option.HasChoiceDetails)
            return string.Empty;

        var lines = new List<TooltipMarkup.Block>();
        foreach (var choice in option.ChoiceDetails)
        {
            if (choice.Pool is { } pool)
            {
                lines.Add(ChoicePoolBlock(pool, Colorize, dayTiers));
                continue;
            }

            var result = ChoiceResultText(choice, dayTiers);

            if (!choice.IsEligible)
            {
                var flat = string.IsNullOrWhiteSpace(result)
                    ? choice.DisplayName
                    : EncounterPreviewText.JoinTooltipLabel(choice.DisplayName, result);
                lines.Add(
                    new TooltipMarkup.Paragraph(
                        $"<color={IneligibleColor}>{flat}</color>",
                        fontSizePercent: 85
                    )
                );
                continue;
            }

            lines.Add(
                new TooltipMarkup.Paragraph(
                    string.IsNullOrWhiteSpace(result)
                        ? $"<color={AccentColor}>{choice.DisplayName}</color>"
                        : EncounterPreviewText.JoinColoredTooltipLabel(
                            choice.DisplayName,
                            Colorize(result),
                            AccentColor
                        )
                )
            );
        }
        return TooltipMarkup.Render(lines);
    }

    public static string BuildQualityLine(GameDataDayTierTable? dayTiers, CardTier? fixedTier)
    {
        string? line = null;
        if (fixedTier.HasValue)
        {
            line = ColorizeTier(fixedTier.Value, EncounterPreviewText.Tier(fixedTier.Value));
        }
        else if (dayTiers != null)
        {
            line = FormatTierDistribution(dayTiers, colorizeTiers: true);
        }

        return string.IsNullOrWhiteSpace(line)
            ? string.Empty
            : TooltipMarkup.Render(new TooltipMarkup.Block[] { new TooltipMarkup.Paragraph(line) });
    }

    public static string BuildRewardQualityLine(
        EncounterRewardFilter? reward,
        string? resultText,
        GameDataDayTierTable? dayTiers
    )
    {
        return reward != null && UsesDayTierDistribution(reward, resultText)
            ? BuildQualityLine(dayTiers, fixedTier: null)
            : string.Empty;
    }

    private static string BuildOutcomes(
        IReadOnlyList<EncounterOutcomeView> outcomes,
        Func<string, string> colorize,
        GameDataDayTierTable? dayTiers
    )
    {
        var items = new List<TooltipMarkup.ListItem>();
        foreach (var outcome in outcomes)
        {
            string content;
            IReadOnlyList<TooltipMarkup.ListItem>? children = null;
            if (outcome.IsCombatPool)
            {
                content = EncounterPreviewText.OutcomeCombatPool(outcome.OptionCount);
            }
            else if (outcome.Details.Count == 0)
            {
                content = EncounterPreviewText.LevelUpRandomPoolSingle(outcome.OptionCount);
            }
            else if (outcome.Details.Count == 1)
            {
                content = DetailLine(outcome.Details[0], colorize, dayTiers);
            }
            else
            {
                content = EncounterPreviewText.OutcomeSubPool(outcome.Details.Count);
                var entries = new List<TooltipMarkup.ListItem>();
                foreach (var detail in outcome.Details)
                    entries.Add(new TooltipMarkup.ListItem(DetailLine(detail, colorize, dayTiers)));
                children = entries;
            }

            if (!outcome.IsEligible)
            {
                items.Add(
                    new TooltipMarkup.ListItem(
                        content,
                        children,
                        fontSizePercent: 85,
                        color: IneligibleColor
                    )
                );
                continue;
            }

            var prefix = outcome.Percent.HasValue
                ? $"<color={AccentColor}>{outcome.Percent.Value}%</color> "
                : string.Empty;
            items.Add(new TooltipMarkup.ListItem($"{prefix}{content}", children));
        }
        return TooltipMarkup.Render(
            new TooltipMarkup.Block[]
            {
                new TooltipMarkup.ListBlock(EncounterPreviewText.OutcomesHeader(), items),
            }
        );
    }

    private static TooltipMarkup.Block ChoicePoolBlock(
        EncounterChoicePool pool,
        Func<string, string> colorize,
        GameDataDayTierTable? dayTiers
    )
    {
        if (pool.IsCombat)
            return new TooltipMarkup.Paragraph(
                $"<color={AccentColor}>{EncounterPreviewText.OutcomeCombatPool(pool.OptionCount)}</color>"
            );

        if (pool.Entries.Count == 0)
            return new TooltipMarkup.Paragraph(
                EncounterPreviewText.LevelUpRandomPoolSingle(pool.OptionCount)
            );

        var entries = new List<TooltipMarkup.ListItem>();
        foreach (var entry in pool.Entries)
        {
            var result = ChoiceResultText(entry, dayTiers);
            entries.Add(
                new TooltipMarkup.ListItem(
                    string.IsNullOrWhiteSpace(result)
                        ? $"<color={AccentColor}>{entry.DisplayName}</color>"
                    : string.IsNullOrWhiteSpace(entry.DisplayName) ? colorize(result)
                    : EncounterPreviewText.JoinColoredTooltipLabel(
                        entry.DisplayName,
                        colorize(result),
                        AccentColor
                    )
                )
            );
        }
        return new TooltipMarkup.ListBlock(
            EncounterPreviewText.OutcomeSubPool(pool.Entries.Count),
            entries
        );
    }

    private static string DetailLine(
        EncounterChoiceDetail detail,
        Func<string, string> colorize,
        GameDataDayTierTable? dayTiers
    )
    {
        var result = ChoiceResultText(detail, dayTiers);
        if (string.IsNullOrWhiteSpace(result))
            return detail.DisplayName;
        return string.IsNullOrWhiteSpace(detail.DisplayName)
            ? colorize(result)
            : EncounterPreviewText.JoinTooltipLabel(detail.DisplayName, colorize(result));
    }

    private static string ChoiceResultText(
        EncounterChoiceDetail choice,
        GameDataDayTierTable? dayTiers
    )
    {
        var result = choice.ResultText;
        if (string.IsNullOrWhiteSpace(result))
            return string.Empty;

        result = EncounterPreviewText.NormalizeRewardSpacing(
            CollapseWhitespace(result.Replace("\r", string.Empty).Replace('\n', ' '))
        );
        var suffix = DayTierSuffix(choice, dayTiers);
        if (suffix == null)
            return result;
        // Full-width punctuation carries its own visual gap; an ASCII space in
        // front of it reads as a hole.
        return suffix.Length > 0 && suffix[0] >= '⺀' ? $"{result}{suffix}" : $"{result} {suffix}";
    }

    private static string CollapseWhitespace(string text)
    {
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
        return builder.ToString();
    }

    // A constraint spanning all currently dealable tiers still follows the day table. Narrower constraints already appear in the reward text.
    private static string? DayTierSuffix(
        EncounterChoiceDetail choice,
        GameDataDayTierTable? dayTiers
    )
    {
        if (
            choice.RewardFilter is not { } reward
            || !UsesDayTierDistribution(reward, choice.ResultText)
        )
            return null;

        if (dayTiers != null)
            return EncounterPreviewText.EncounterTierDistributionSuffix(
                FormatTierDistribution(dayTiers, colorizeTiers: false)
            );
        return null;
    }

    private static bool UsesDayTierDistribution(EncounterRewardFilter reward, string? resultText)
    {
        if (!reward.UsesDayTierDistribution)
            return false;

        var tiers = reward.Tiers;
        return (tiers.Count == 0 || tiers.Count >= DealableTiers.Length)
            && !HasExplicitTierDescriptor(resultText);
    }

    private static string FormatTierDistribution(
        GameDataDayTierTable distribution,
        bool colorizeTiers
    )
    {
        var entries = new List<string>(distribution.Entries.Count);
        foreach (var entry in distribution.Entries)
        {
            var text =
                $"{EncounterPreviewText.Tier(entry.Tier)} {entry.Percent.ToString("0.##", CultureInfo.InvariantCulture)}%";
            entries.Add(colorizeTiers ? ColorizeTier(entry.Tier, text) : text);
        }
        return string.Join(" · ", entries);
    }

    private static string ColorizeTier(CardTier tier, string text) =>
        $"<color=#{EncounterPreviewText.TierColorHex(tier)}>{text}</color>";

    private static bool HasExplicitTierDescriptor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var tier in Enum.GetValues(typeof(CardTier)))
        {
            if (tier is CardTier value && HasExplicitTierDescriptor(text, value))
                return true;
        }

        return false;
    }

    private static bool HasExplicitTierDescriptor(string text, CardTier tier)
    {
        var tierName = tier.ToString();
        if (HasEnglishTierDescriptor(text, tierName))
            return true;
        if (ContainsOrdinalIgnoreCase(text, $"({tierName})"))
            return true;

        // The result text is written in the game language's script, which is independent
        // of the BPP locale mode, so both Chinese variants are always tested.
        var (_, chineseMainland, chineseTraditional) = EncounterPreviewText.TierForms(tier);
        return HasChineseTierDescriptor(text, chineseMainland)
            || HasChineseTierDescriptor(text, chineseTraditional);
    }

    private static bool HasChineseTierDescriptor(string text, string tierWord)
    {
        if (string.IsNullOrEmpty(tierWord))
            return false;

        return ContainsOrdinalIgnoreCase(text, $"{tierWord}级")
            || ContainsOrdinalIgnoreCase(text, $"{tierWord}級")
            || ContainsOrdinalIgnoreCase(text, $"{tierWord}階")
            || ContainsOrdinalIgnoreCase(text, $"{tierWord}品质")
            || ContainsOrdinalIgnoreCase(text, $"{tierWord}品質")
            || ContainsOrdinalIgnoreCase(text, $"（{tierWord}）");
    }

    private static bool HasEnglishTierDescriptor(string text, string tierName)
    {
        var index = 0;
        while (index < text.Length)
        {
            index = text.IndexOf(tierName, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            if (IsAsciiWordBoundary(text, index - 1))
            {
                var cursor = index + tierName.Length;
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                    cursor++;
                if (cursor < text.Length && text[cursor] == '-')
                    cursor++;
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                    cursor++;

                const string TierSuffix = "tier";
                if (
                    cursor + TierSuffix.Length <= text.Length
                    && string.Compare(
                        text,
                        cursor,
                        TierSuffix,
                        0,
                        TierSuffix.Length,
                        StringComparison.OrdinalIgnoreCase
                    ) == 0
                    && IsAsciiWordBoundary(text, cursor + TierSuffix.Length)
                )
                    return true;
            }

            index += tierName.Length;
        }

        return false;
    }

    private static bool IsAsciiWordBoundary(string text, int index)
    {
        if (index < 0 || index >= text.Length)
            return true;

        var character = text[index];
        return !(
            character >= 'a' && character <= 'z'
            || character >= 'A' && character <= 'Z'
            || character >= '0' && character <= '9'
            || character == '_'
        );
    }

    private static bool ContainsOrdinalIgnoreCase(string text, string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
