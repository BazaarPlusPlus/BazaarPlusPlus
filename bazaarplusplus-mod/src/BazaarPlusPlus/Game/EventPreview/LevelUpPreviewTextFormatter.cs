#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.EventPreview;

// Content for the BPP section appended below the native "next level rewards" tooltip:
// the max-health gain plus the rewards the player can actually receive, resolved from
// the level-up spawn groups. Weighted single-reward groups are random alternatives, so
// they collapse into one "One of: A / B" sentence instead of one line each; random
// pools become a count summary. Board-conditional bonus groups (e.g. "Inspired by ..."
// skills gated on specific board cards) are omitted.
internal static class LevelUpPreviewTextFormatter
{
    private const string AccentColor = "#FFD37E";

    // Board (carpet) slot unlocks are server-driven and not in TLevelUp; the native
    // tooltip hardcodes the same facts (a static "2" on the carpet icon, grayed once
    // the current level reaches 4), so mirror those constants here.
    private const int BoardSlotsPerLevel = 2;
    private const int LastBoardSlotLevel = 4;

    public static string Build(
        LevelUpPreviewPlan? levelUp,
        Func<Guid, EncounterPreviewTemplatePlan?> resolveTemplate,
        EHero? currentHero,
        Func<string, string>? colorizeResult = null,
        int? currentLevel = null
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

        // Level-up rewards are a selection screen (LevelUpState allows
        // SelectItem/SelectSkill/SelectEncounter): every spawned group contributes
        // candidates and the player picks one — the native pack icon's "1". Render all
        // candidates as one "Choose one:" list; random pools contribute a count entry.
        var candidates = new List<TooltipMarkup.ListItem>();
        if (levelUp.Groups.Count > 0)
        {
            // Random selection (levels 9/18): groups whose weight equals their card
            // count are uniform per-card rolls competing for one offered slot — the
            // enchant reward is split across such groups only to encode card counts
            // (w2×[Yetarian, Sanguine] + w1×[Arcane] = one random enchant of three).
            // Merge them into a single pool candidate instead of listing each group.
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
                CollectGroup(candidates, group, resolveTemplate, currentHero, Colorize);
            }
            if (uniformPool.Count > 0)
                CollectCandidates(
                    candidates,
                    uniformPool,
                    limit: 1,
                    resolveTemplate,
                    currentHero,
                    Colorize
                );
        }

        // Sequential screens may roll several slots from the same pool (level 7
        // offers two teacher slots over one 10-id pool, level 16 three); repeating
        // the identical block adds nothing to "what can I get" — keep one.
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

    // A weighted group whose weight equals its card count: every card is a uniform
    // roll for the same offered slot, so such groups merge into one pool.
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
        EHero? currentHero,
        Func<string, string> colorize
    )
    {
        if (!PassesPrerequisites(group, currentHero))
            return;

        var ids = new List<Guid>(group.TemplateIds);

        if (ids.Count == 0)
            return;

        CollectCandidates(candidates, ids, group.Limit, resolveTemplate, currentHero, colorize);
    }

    private static void CollectCandidates(
        List<TooltipMarkup.ListItem> candidates,
        List<Guid> ids,
        int limit,
        Func<Guid, EncounterPreviewTemplatePlan?> resolveTemplate,
        EHero? currentHero,
        Func<string, string> colorize
    )
    {
        // Spawn filtering also honours each reward card's own Heroes field (the group's
        // run prerequisite alone is coarser: e.g. "Core Initialization" sits in a
        // Dooley-or-Jules group but the card itself is Dooley-only). Unresolvable
        // templates cannot be judged and stay counted.
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

        // Pools that filter down to one concrete reward render as that reward, not
        // as a "one of 1:" wrapper.
        if (ids.Count == 1 || (unresolved == 0 && eligible.Count == 1))
        {
            if (eligible.Count == 0)
                return;

            var template = eligible[0];
            var title = EventPreviewLocalization.ResolveTitle(template) ?? template.InternalName;
            var description = EventPreviewLocalization.ResolveDescription(template);
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

        // Inside a choose-one list a single draw needs no "x1" marker.
        var draws = Math.Min(limit, optionCount);

        // After hero filtering most pools shrink to a handful of concrete rewards;
        // list those out instead of hiding them behind a count.
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
                    EventPreviewLocalization.ResolveTitle(template) ?? template.InternalName;
                var entryDescription = EventPreviewLocalization
                    .ResolveDescription(template)
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

    // Only hero conditions are evaluated; groups gated on board state ("Inspired by"
    // bonus skills) are omitted, and unknown run conditions — including an unknown
    // current hero — keep the group visible rather than silently dropping rewards.
    private static bool PassesPrerequisites(LevelUpPreviewGroup group, EHero? currentHero)
    {
        foreach (var condition in group.HeroConditions)
            if (!PassesHeroCondition(condition, currentHero))
                return false;

        return true;
    }

    private static bool PassesHeroCondition(
        LevelUpPreviewHeroCondition condition,
        EHero? currentHero
    )
    {
        // Hero detection failed: keep hero-gated groups visible (consistent with
        // EncounterHeroEligibility) instead of hiding them all.
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
