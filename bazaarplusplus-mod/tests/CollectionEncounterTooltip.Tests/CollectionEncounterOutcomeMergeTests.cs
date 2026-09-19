using Xunit;

namespace EncounterTooltip.Tests;

public class EncounterOutcomeMergeTests
{
    private static readonly Guid MonsterA = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid MonsterB = Guid.Parse("a0000000-0000-0000-0000-000000000002");

    [Fact]
    public void Combat_pools_with_the_same_eligibility_merge_into_one_view()
    {
        // Mountain Pass shape: 25% fight + 25% fight + 40% loot + 10% skill; the
        // two combat groups carry no visible monster names, so they collapse to 50%.
        var views = EncounterEventDetailResolver.BuildOutcomeViews(
            new List<EncounterEventDetailResolver.OutcomeGroupResolution>
            {
                Combat(weight: 25, ids: new[] { MonsterA }),
                Combat(weight: 25, ids: new[] { MonsterB }),
                Rewards(weight: 40, text: "Get a big thing"),
                Rewards(weight: 10, text: "Get a small thing"),
            },
            totalWeight: 100
        );

        var combat = Assert.Single(views, view => view.IsCombatPool);
        Assert.Equal(50, combat.Percent);
        Assert.Equal(2, combat.OptionCount);
        Assert.Equal(3, views.Count);
        // Merged combat keeps the first combat group's position.
        Assert.True(views[0].IsCombatPool);
    }

    [Fact]
    public void Merged_combat_percent_rounds_the_summed_weight_not_the_parts()
    {
        var views = EncounterEventDetailResolver.BuildOutcomeViews(
            new List<EncounterEventDetailResolver.OutcomeGroupResolution>
            {
                Combat(weight: 33, ids: new[] { MonsterA }),
                Combat(weight: 33, ids: new[] { MonsterB }),
                Rewards(weight: 33, text: "Get a thing"),
            },
            totalWeight: 99
        );

        // 66/99 = 66.67% -> 67, not round(33.3) + round(33.3) = 66.
        Assert.Equal(67, views.Single(view => view.IsCombatPool).Percent);
    }

    [Fact]
    public void Overlapping_monster_pools_do_not_inflate_the_option_count()
    {
        var views = EncounterEventDetailResolver.BuildOutcomeViews(
            new List<EncounterEventDetailResolver.OutcomeGroupResolution>
            {
                Combat(weight: 50, ids: new[] { MonsterA, MonsterB }),
                Combat(weight: 50, ids: new[] { MonsterB }),
            },
            totalWeight: 100
        );

        var combat = Assert.Single(views);
        Assert.Equal(2, combat.OptionCount);
        Assert.Equal(100, combat.Percent);
    }

    [Fact]
    public void Ineligible_combat_pools_stay_separate_from_eligible_ones()
    {
        var views = EncounterEventDetailResolver.BuildOutcomeViews(
            new List<EncounterEventDetailResolver.OutcomeGroupResolution>
            {
                Combat(weight: 60, ids: new[] { MonsterA }),
                Combat(weight: 40, ids: new[] { MonsterB }, eligible: false),
            },
            totalWeight: 60
        );

        Assert.Equal(2, views.Count);
        Assert.Equal(100, views[0].Percent);
        Assert.False(views[1].IsEligible);
        Assert.Null(views[1].Percent);
    }

    private static EncounterEventDetailResolver.OutcomeGroupResolution Combat(
        uint weight,
        Guid[] ids,
        bool eligible = true
    ) =>
        new(
            weight,
            eligible,
            isCombatPool: true,
            new HashSet<Guid>(ids),
            new List<EncounterChoiceDetail>()
        );

    private static EncounterEventDetailResolver.OutcomeGroupResolution Rewards(
        uint weight,
        string text
    ) =>
        new(
            weight,
            eligible: true,
            isCombatPool: false,
            new HashSet<Guid>(),
            new List<EncounterChoiceDetail>
            {
                new(Guid.NewGuid(), displayName: "Reward", resultText: text, rewardFilter: null),
            }
        );

    // Farai/Underground Resistance: dozens of title-only package outcomes collapse
    // into one pooled line with the summed probability.
    [Fact]
    public void Large_title_only_clusters_collapse_into_one_pool_view()
    {
        var resolutions = new List<EncounterEventDetailResolver.OutcomeGroupResolution>();
        for (var i = 0; i < 10; i++)
            resolutions.Add(TitleOnly(weight: 3, title: $"NPC {i}'s Package"));
        resolutions.Add(Rewards(weight: 10, text: "Gain 5 Gold"));

        var views = EncounterEventDetailResolver.BuildOutcomeViews(resolutions, totalWeight: 40);

        Assert.Equal(2, views.Count);
        Assert.Equal(10, views[0].OptionCount);
        Assert.Empty(views[0].Details);
        Assert.Equal(75, views[0].Percent);
        Assert.Equal(25, views[1].Percent);
    }

    [Fact]
    public void Large_ineligible_title_only_clusters_collapse_into_one_dimmed_pool_view()
    {
        var resolutions = new List<EncounterEventDetailResolver.OutcomeGroupResolution>();
        for (var i = 0; i < 10; i++)
            resolutions.Add(TitleOnly(weight: 3, title: $"NPC {i}'s Package", eligible: false));

        var view = Assert.Single(
            EncounterEventDetailResolver.BuildOutcomeViews(resolutions, totalWeight: 0)
        );

        Assert.False(view.IsEligible);
        Assert.Null(view.Percent);
        Assert.Equal(10, view.OptionCount);
        Assert.Empty(view.Details);
    }

    [Fact]
    public void Large_title_only_clusters_with_different_eligibility_collapse_separately()
    {
        var resolutions = new List<EncounterEventDetailResolver.OutcomeGroupResolution>();
        for (var i = 0; i < 10; i++)
        {
            if (i < 9)
                resolutions.Add(TitleOnly(weight: 1, title: $"Available {i}"));
            resolutions.Add(TitleOnly(weight: 1, title: $"Unavailable {i}", eligible: false));
        }

        var views = EncounterEventDetailResolver.BuildOutcomeViews(resolutions, totalWeight: 9);

        Assert.Equal(2, views.Count);
        var eligible = views[0];
        Assert.True(eligible.IsEligible);
        Assert.Equal(100, eligible.Percent);
        Assert.Equal(9, eligible.OptionCount);
        var ineligible = views[1];
        Assert.False(ineligible.IsEligible);
        Assert.Null(ineligible.Percent);
        Assert.Equal(10, ineligible.OptionCount);
    }

    // Shops (limit > 1) with nothing but nameless pools or a single view describe
    // stock composition, not outcome odds; one-shot rolls (limit 1) always render.
    [Fact]
    public void Multi_spawn_events_without_real_alternatives_suppress()
    {
        var single = new List<EncounterOutcomeView> { View(100, "Random item", nameless: true) };
        var namelessPair = new List<EncounterOutcomeView>
        {
            View(50, "Random item", nameless: true),
            View(50, "Random item", nameless: true),
        };
        var named = new List<EncounterOutcomeView>
        {
            View(10, "Treasure Chest", nameless: false),
            View(90, "Random item", nameless: true),
        };

        Assert.True(EncounterEventDetailResolver.ShouldSuppressOutcomeViews(single, 2));
        Assert.True(EncounterEventDetailResolver.ShouldSuppressOutcomeViews(namelessPair, 3));
        Assert.False(EncounterEventDetailResolver.ShouldSuppressOutcomeViews(named, 2));
        Assert.False(EncounterEventDetailResolver.ShouldSuppressOutcomeViews(namelessPair, 1));
    }

    private static EncounterOutcomeView View(int percent, string text, bool nameless) =>
        new(
            percent,
            isEligible: true,
            isCombatPool: false,
            optionCount: 1,
            new[]
            {
                new EncounterChoiceDetail(
                    Guid.NewGuid(),
                    displayName: nameless ? string.Empty : text,
                    resultText: nameless ? text : string.Empty,
                    rewardFilter: null
                ),
            }
        );

    private static EncounterEventDetailResolver.OutcomeGroupResolution TitleOnly(
        uint weight,
        string title,
        bool eligible = true
    ) =>
        new(
            weight,
            eligible,
            isCombatPool: false,
            new HashSet<Guid>(),
            new List<EncounterChoiceDetail>
            {
                new(
                    Guid.NewGuid(),
                    displayName: title,
                    resultText: string.Empty,
                    rewardFilter: null
                ),
            }
        );

    // Same rendered content in several weighted groups (Farai's per-NPC package
    // split) collapses into one line with the weights summed.
    [Fact]
    public void Same_content_groups_merge_with_summed_weights()
    {
        var views = EncounterEventDetailResolver.BuildOutcomeViews(
            new List<EncounterEventDetailResolver.OutcomeGroupResolution>
            {
                Rewards(weight: 2, text: "Aila's Package"),
                Rewards(weight: 1, text: "Aila's Package"),
                Rewards(weight: 3, text: "Barkun's Package"),
            },
            totalWeight: 6
        );

        Assert.Equal(2, views.Count);
        Assert.Equal(50, views[0].Percent);
        Assert.Equal(50, views[1].Percent);
    }
}
