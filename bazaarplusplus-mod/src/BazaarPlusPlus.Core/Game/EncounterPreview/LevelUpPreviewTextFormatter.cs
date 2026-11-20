#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.EncounterPreview;

public static class LevelUpPreviewTextFormatter
{
    private const string AccentColor = "#FFD37E";

    // The server owns board slot unlocks and TLevelUp omits them. Mirror the native carpet tooltip's two slots per level until level 4.
    private const int BoardSlotsPerLevel = 2;
    private const int LastBoardSlotLevel = 4;

    public static string Build(
        LevelUpPreviewPlan? levelUp,
        Func<Guid, EncounterPreviewTemplatePlan?> resolveTemplate,
        CardHero? currentHero,
        Func<string, string>? colorizeResult = null,
        int? currentLevel = null,
        LiveAbilityValueResolver? resolveLiveValue = null
    )
    {
        if (levelUp == null)
            return string.Empty;

        var rawColorize = colorizeResult ?? (text => text);
        string Colorize(string text) => TooltipMarkup.NormalizeInlineFragment(rawColorize(text));

        var lines = new List<TooltipMarkup.Block>();
        if (levelUp.HealthIncrease > 0)
            lines.Add(
                new TooltipMarkup.Paragraph(
                    Colorize(EncounterPreviewText.LevelUpMaxHealth((int)levelUp.HealthIncrease))
                )
            );
        if (currentLevel.HasValue && currentLevel.Value < LastBoardSlotLevel)
            lines.Add(
                new TooltipMarkup.Paragraph(
                    Colorize(EncounterPreviewText.LevelUpBoardSlots(BoardSlotsPerLevel))
                )
            );

        // LevelUpState offers candidates from every spawned group on one screen, and the player selects one.
        var candidates = new List<TooltipMarkup.ListItem>();
        if (levelUp.Groups.Count > 0)
        {
            // When each group weighs as much as its card count, every card competes for the same slot at equal odds.
            var uniformPool = new List<Guid>();
            foreach (var group in levelUp.Groups)
            {
                if (
                    levelUp.IsRandomSelection
                    && PassesPrerequisites(group, currentHero)
                    && UniformPoolIds(group) is { } poolIds
                )
                {
                    uniformPool.AddRange(poolIds);
                    continue;
                }
                CollectGroup(
                    candidates,
                    group,
                    resolveTemplate,
                    currentHero,
                    Colorize,
                    resolveLiveValue
                );
            }
            if (uniformPool.Count > 0)
                CollectCandidates(
                    candidates,
                    uniformPool,
                    limit: 1,
                    resolveTemplate,
                    currentHero,
                    Colorize,
                    resolveLiveValue
                );
        }

        // Sequential rewards can draw several slots from the same pool, so repeat draws share one candidate list.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        candidates.RemoveAll(candidate =>
            !seen.Add(
                TooltipMarkup.Render(
                    new TooltipMarkup.Block[]
                    {
                        new TooltipMarkup.ListBlock(candidate.Content, candidate.Children),
                    }
                )
            )
        );

        if (candidates.Count == 1)
        {
            var candidate = candidates[0];
            lines.Add(
                candidate.Children.Count == 0
                    ? new TooltipMarkup.Paragraph(candidate.Content)
                    : new TooltipMarkup.ListBlock(candidate.Content, candidate.Children)
            );
        }
        else if (candidates.Count > 1)
        {
            lines.Add(new TooltipMarkup.ListBlock(EncounterPreviewText.LevelUpOneOf(), candidates));
        }

        return TooltipMarkup.Render(lines);
    }

    private static List<Guid>? UniformPoolIds(LevelUpPreviewGroup group)
    {
        if (group.RandomWeight == 0)
            return null;

        var ids = new List<Guid>(group.TemplateIds);
        return ids.Count > 0 && group.RandomWeight == ids.Count ? ids : null;
    }

    private static void CollectGroup(
        List<TooltipMarkup.ListItem> candidates,
        LevelUpPreviewGroup group,
        Func<Guid, EncounterPreviewTemplatePlan?> resolveTemplate,
        CardHero? currentHero,
        Func<string, string> colorize,
        LiveAbilityValueResolver? resolveLiveValue
    )
    {
        if (!PassesPrerequisites(group, currentHero))
            return;

        var ids = new List<Guid>(group.TemplateIds);

        if (ids.Count == 0)
            return;

        CollectCandidates(
            candidates,
            ids,
            group.Limit,
            resolveTemplate,
            currentHero,
            colorize,
            resolveLiveValue
        );
    }

    private static void CollectCandidates(
        List<TooltipMarkup.ListItem> candidates,
        List<Guid> ids,
        int limit,
        Func<Guid, EncounterPreviewTemplatePlan?> resolveTemplate,
        CardHero? currentHero,
        Func<string, string> colorize,
        LiveAbilityValueResolver? resolveLiveValue
    )
    {
        // A reward card's Heroes can be narrower than its group's prerequisite. Unknown templates remain in the count.
        var eligible = new List<EncounterPreviewTemplatePlan>();
        var unresolved = 0;
        foreach (var id in ids)
        {
            var template = resolveTemplate(id);
            if (template == null)
                unresolved++;
            else if (EncounterHeroEligibility.Matches(template.Heroes, currentHero))
                eligible.Add(template);
        }

        if (ids.Count == 1 || (unresolved == 0 && eligible.Count == 1))
        {
            if (eligible.Count == 0)
                return;

            var template = eligible[0];
            var title =
                EncounterPreviewLocalization.ResolveTitle(template) ?? template.InternalName;
            var description = EncounterPreviewLocalization.ResolveDescription(
                template,
                resolveLiveValue
            );
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
                return;

            if (string.IsNullOrWhiteSpace(description))
                candidates.Add(new TooltipMarkup.ListItem($"<color={AccentColor}>{title}</color>"));
            else if (string.IsNullOrWhiteSpace(title))
                candidates.Add(
                    new TooltipMarkup.ListItem(
                        colorize(EncounterPreviewText.NormalizeRewardSpacing(description!))
                    )
                );
            else
                candidates.Add(
                    new TooltipMarkup.ListItem(
                        EncounterPreviewText.JoinColoredTooltipLabel(
                            title!,
                            colorize(EncounterPreviewText.NormalizeRewardSpacing(description!)),
                            AccentColor
                        )
                    )
                );
            return;
        }

        var optionCount = eligible.Count + unresolved;
        if (optionCount == 0)
            return;

        var draws = Math.Min(limit, optionCount);

        if (unresolved == 0 && eligible.Count <= 8)
        {
            var header =
                draws == 1
                    ? EncounterPreviewText.OutcomeSubPool(eligible.Count)
                    : EncounterPreviewText.LevelUpRandomPool(draws, eligible.Count);
            var entries = new List<TooltipMarkup.ListItem>();
            foreach (var template in eligible)
            {
                var entryTitle =
                    EncounterPreviewLocalization.ResolveTitle(template) ?? template.InternalName;
                var entryDescription = EncounterPreviewLocalization
                    .ResolveDescription(template, resolveLiveValue)
                    ?.Replace("\r", string.Empty)
                    .Replace('\n', ' ');
                entries.Add(
                    new TooltipMarkup.ListItem(
                        string.IsNullOrWhiteSpace(entryDescription)
                            ? $"<color={AccentColor}>{entryTitle}</color>"
                            : EncounterPreviewText.JoinColoredTooltipLabel(
                                entryTitle!,
                                colorize(
                                    EncounterPreviewText.NormalizeRewardSpacing(entryDescription!)
                                ),
                                AccentColor
                            )
                    )
                );
            }
            candidates.Add(new TooltipMarkup.ListItem(header, entries));
            return;
        }

        candidates.Add(
            new TooltipMarkup.ListItem(
                draws == 1
                    ? EncounterPreviewText.LevelUpRandomPoolSingle(optionCount)
                    : EncounterPreviewText.LevelUpRandomPool(draws, optionCount)
            )
        );
    }

    private static bool PassesPrerequisites(LevelUpPreviewGroup group, CardHero? currentHero)
    {
        foreach (var condition in group.HeroConditions)
            if (!PassesHeroCondition(condition, currentHero))
                return false;

        return true;
    }

    private static bool PassesHeroCondition(
        LevelUpPreviewHeroCondition condition,
        CardHero? currentHero
    )
    {
        // If hero detection fails, keep hero-gated rewards visible.
        if (currentHero == null)
            return true;

        var contains = condition.Heroes.Contains(currentHero.Value);
        return condition.ComparisonOperator switch
        {
            "None" => !contains,
            _ => contains,
        };
    }
}
