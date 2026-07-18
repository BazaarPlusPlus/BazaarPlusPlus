#nullable enable
using System.Globalization;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.Encounters;

namespace BazaarPlusPlus.Game.EventPreview;

// Content for the BPP section cloned into the native encounter tooltip: one line per
// choice, accent-colored name plus the game's own result text (run through the
// supplied colorizer for native keyword coloring). Card rewards whose pool tier is
// day-driven get the current GameData tier distribution appended to the text.
internal static class EncounterPreviewTextFormatter
{
    private const string AccentColor = "#FFD37E";
    private const string IneligibleColor = "#8F8268";

    private static readonly ETier[] DealableTiers =
    {
        ETier.Bronze,
        ETier.Silver,
        ETier.Gold,
        ETier.Diamond,
    };

    public static string Build(
        EncounterOption option,
        Func<string, string>? colorizeResult = null,
        ETier? dayTierCeiling = null,
        TierDistribution? dayTierDistribution = null
    )
    {
        if (option == null)
            return string.Empty;

        var rawColorize = colorizeResult ?? (text => text);
        string Colorize(string text) => TooltipMarkup.NormalizeInlineFragment(rawColorize(text));

        if (option.HasOutcomeGroups)
            return BuildOutcomes(
                option.OutcomeGroups!,
                Colorize,
                dayTierCeiling,
                dayTierDistribution
            );
        if (!option.HasChoiceDetails)
            return string.Empty;

        var lines = new List<TooltipMarkup.Block>();
        foreach (var choice in option.ChoiceDetails)
        {
            // Rolled pools ("Advanced Training" trainings, "Epic Battle" monsters)
            // render as one summary line instead of one line per member.
            if (choice.Pool is { } pool)
            {
                lines.Add(ChoicePoolBlock(pool, Colorize, dayTierCeiling, dayTierDistribution));
                continue;
            }

            var result = ChoiceResultText(choice, dayTierCeiling, dayTierDistribution);

            // Prerequisite-unmet options render as one flat dimmed line (no accent, no
            // keyword coloring) at reduced size; the resolver already sorted them last.
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

    public static string BuildQualityLine(
        TierDistribution? dayTierDistribution,
        ETier? fixedTier,
        ETier? dayTierCeiling
    )
    {
        string? line = null;
        if (fixedTier.HasValue)
        {
            line = ColorizeTier(fixedTier.Value, EncounterPreviewText.Tier(fixedTier.Value));
        }
        else if (dayTierDistribution != null)
        {
            line = FormatTierDistribution(dayTierDistribution, colorizeTiers: true);
        }
        else if (dayTierCeiling.HasValue)
        {
            line = EncounterPreviewText.EncounterTierCeilingLine(dayTierCeiling.Value);
        }

        return string.IsNullOrWhiteSpace(line)
            ? string.Empty
            : TooltipMarkup.Render(new TooltipMarkup.Block[] { new TooltipMarkup.Paragraph(line) });
    }

    public static string BuildRewardQualityLine(
        EncounterRewardFilter? reward,
        string? resultText,
        TierDistribution? dayTierDistribution,
        ETier? dayTierCeiling
    )
    {
        return reward != null && UsesDayTierDistribution(reward, resultText)
            ? BuildQualityLine(dayTierDistribution, fixedTier: null, dayTierCeiling: dayTierCeiling)
            : string.Empty;
    }

    // Random-outcome events: one block per rolled alternative with its normalized
    // probability; prerequisite-unmet groups render dimmed without a percentage.
    private static string BuildOutcomes(
        IReadOnlyList<EncounterOutcomeView> outcomes,
        Func<string, string> colorize,
        ETier? dayTierCeiling,
        TierDistribution? dayTierDistribution
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
                // Collapsed same-shaped cluster (Farai's NPC packages): count only.
                content = EncounterPreviewText.LevelUpRandomPoolSingle(outcome.OptionCount);
            }
            else if (outcome.Details.Count == 1)
            {
                content = DetailLine(
                    outcome.Details[0],
                    colorize,
                    dayTierCeiling,
                    dayTierDistribution
                );
            }
            else
            {
                content = EncounterPreviewText.OutcomeSubPool(outcome.Details.Count);
                var entries = new List<TooltipMarkup.ListItem>();
                foreach (var detail in outcome.Details)
                    entries.Add(
                        new TooltipMarkup.ListItem(
                            DetailLine(detail, colorize, dayTierCeiling, dayTierDistribution)
                        )
                    );
                children = entries;
            }

            // Ownership-gated groups render dimmed; their own text already carries
            // the condition ("(if you have Powder Keg or the Big One)").
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

    // One rolled-pool choice line: combat roll, small expandable entry list (with
    // accent-colored names to match sibling choice lines), or a bare option count.
    private static TooltipMarkup.Block ChoicePoolBlock(
        EncounterChoicePool pool,
        Func<string, string> colorize,
        ETier? dayTierCeiling,
        TierDistribution? dayTierDistribution
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
            var result = ChoiceResultText(entry, dayTierCeiling, dayTierDistribution);
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

    // One outcome entry: "Name: result", bare name, or bare result — query-pool
    // summaries have no card name, so their result text stands alone.
    private static string DetailLine(
        EncounterChoiceDetail detail,
        Func<string, string> colorize,
        ETier? dayTierCeiling,
        TierDistribution? dayTierDistribution
    )
    {
        var result = ChoiceResultText(detail, dayTierCeiling, dayTierDistribution);
        if (string.IsNullOrWhiteSpace(result))
            return detail.DisplayName;
        return string.IsNullOrWhiteSpace(detail.DisplayName)
            ? colorize(result)
            : EncounterPreviewText.JoinTooltipLabel(detail.DisplayName, colorize(result));
    }

    // Flattens embedded newlines (descriptions render as list entries) and appends
    // the day-tier suffix where the pool is day-driven.
    private static string ChoiceResultText(
        EncounterChoiceDetail choice,
        ETier? dayTierCeiling,
        TierDistribution? dayTierDistribution
    )
    {
        var result = choice.ResultText;
        if (string.IsNullOrWhiteSpace(result))
            return string.Empty;

        result = EncounterPreviewText.NormalizeRewardSpacing(
            CollapseWhitespace(result.Replace("\r", string.Empty).Replace('\n', ' '))
        );
        var suffix = DayTierSuffix(choice, dayTierCeiling, dayTierDistribution);
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

    // A pool with no tier constraint (or one spanning every dealable tier) is day-driven:
    // show the runtime GameData distribution when available, otherwise retain the old
    // ceiling summary as a fallback. Narrow constraints already state the tier explicitly.
    private static string? DayTierSuffix(
        EncounterChoiceDetail choice,
        ETier? dayTierCeiling,
        TierDistribution? dayTierDistribution
    )
    {
        if (
            choice.RewardFilter is not { } reward
            || !UsesDayTierDistribution(reward, choice.ResultText)
        )
            return null;

        if (dayTierDistribution != null)
            return EncounterPreviewText.EncounterTierDistributionSuffix(
                FormatTierDistribution(dayTierDistribution, colorizeTiers: false)
            );

        if (!dayTierCeiling.HasValue)
            return null;

        var ceilingRank = TierOrder.Rank(dayTierCeiling.Value);
        var effective = new List<ETier>();
        foreach (var tier in DealableTiers)
            if (TierOrder.Rank(tier) <= ceilingRank)
                effective.Add(tier);

        if (effective.Count == 0)
            return null;
        return effective.Count == 1
            ? EncounterPreviewText.EncounterTierExact(effective[0])
            : EncounterPreviewText.EncounterDayTierSuffix(effective[^1]);
    }

    private static bool UsesDayTierDistribution(EncounterRewardFilter reward, string? resultText)
    {
        if (!reward.UsesDayTierDistribution)
            return false;

        var tiers = reward.Tiers;
        return (tiers.Count == 0 || tiers.Count >= DealableTiers.Length)
            && !HasExplicitTierDescriptor(resultText);
    }

    private static string FormatTierDistribution(TierDistribution distribution, bool colorizeTiers)
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

    private static string ColorizeTier(ETier tier, string text) =>
        $"<color=#{EncounterPreviewText.TierColorHex(tier)}>{text}</color>";

    private static bool HasExplicitTierDescriptor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var tier in Enum.GetValues(typeof(ETier)))
        {
            if (tier is ETier value && HasExplicitTierDescriptor(text, value))
                return true;
        }

        return false;
    }

    private static bool HasExplicitTierDescriptor(string text, ETier tier)
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
