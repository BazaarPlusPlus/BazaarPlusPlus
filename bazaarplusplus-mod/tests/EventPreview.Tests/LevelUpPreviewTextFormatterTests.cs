using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Localization;
using Xunit;

namespace EventPreview.Tests;

// Drives the formatter over compiled level-up plans; how the compiler builds those plans from
// the game's TLevelUp rewards is CollectionEncounterTooltip.Tests' LevelUpPreviewCompilerTests.
public sealed class LevelUpPreviewTextFormatterTests
{
    public LevelUpPreviewTextFormatterTests()
    {
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);
    }

    [Fact]
    public void Build_renders_health_pools_and_hero_filtered_groups()
    {
        var poolIds = Enumerable
            .Range(0, 25)
            .Select(i => Guid.Parse($"20000000-0000-0000-0000-{i:d12}"))
            .ToList();
        // The compiler drops a board-conditional bonus group (a card-count prerequisite), so
        // the plan carries only the groups below.
        var plan = Plan(
            level: 2,
            healthIncrease: 150,
            isRandomSelection: false,
            // Hero-excluded group (current hero Vanessa is not Dooley/Jules).
            Group(
                new[] { Guid.Parse("30000000-0000-0000-0000-000000000001") },
                limit: 1,
                Heroes("None", CardHero.Dooley, CardHero.Jules)
            ),
            // Hero-mismatched group (requires Dooley/Jules).
            Group(
                new[] { Guid.Parse("30000000-0000-0000-0000-000000000002") },
                limit: 1,
                Heroes("Any", CardHero.Dooley, CardHero.Jules)
            ),
            // Random pool.
            Group(poolIds, limit: 3)
        );

        var text = LevelUpPreviewTextFormatter.Build(
            plan,
            resolveTemplate: _ => null,
            currentHero: CardHero.Vanessa,
            colorizeResult: result => $"<line-height=1.6em>«{result}»</line-height>",
            currentLevel: 2
        );

        Assert.Contains("«+ 150 Max Health»", text);
        Assert.Contains("«+ 2 board slots»", text);
        Assert.Contains("3× random reward (25 options)", text);
        Assert.Contains("<line-height=1.9em>\n<line-height=1.4em>", text);
        Assert.DoesNotContain("<line-height=1.6em>«", text);
        // Single-id groups with unresolvable templates and filtered groups add nothing.
        Assert.DoesNotContain("30000000", text);
    }

    [Fact]
    public void Build_returns_empty_for_null_level_up()
    {
        Assert.Equal(
            string.Empty,
            LevelUpPreviewTextFormatter.Build(null, _ => null, CardHero.Vanessa)
        );
    }

    [Fact]
    public void Build_uses_live_values_for_dynamic_reward_totals()
    {
        var rewardId = Guid.Parse("30000000-0000-0000-0000-0000000000d1");
        var plan = Plan(
            level: 3,
            healthIncrease: 0,
            isRandomSelection: false,
            Group(new[] { rewardId }, limit: 1)
        );
        var reward = new EncounterPreviewTemplatePlan(
            rewardId,
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<CardHero>(),
            "Prosperous Estates",
            new EncounterPreviewLocalizedText(null, "Prosperous Estates"),
            new EncounterPreviewLocalizedText(
                null,
                "Gain {ability.0.mod} Gold for each Property you have (including Stash) [{ability.0}]"
            ),
            new Dictionary<string, EncounterPreviewAbilityValue> { ["0.mod"] = new("5") },
            rewardFilter: null
        );

        var text = LevelUpPreviewTextFormatter.Build(
            plan,
            id => id == rewardId ? reward : null,
            currentHero: null,
            resolveLiveValue: ResolveLiveValue
        );

        Assert.Contains("Gain 5 Gold for each Property you have (including Stash) [15]", text);

        bool ResolveLiveValue(
            Guid templateId,
            string effectId,
            bool isAura,
            out string valueText,
            out string? unit
        )
        {
            valueText = templateId == rewardId && effectId == "0" && !isAura ? "15" : string.Empty;
            unit = null;
            return valueText.Length > 0;
        }
    }

    [Fact]
    public void Build_uses_chinese_colon_without_a_following_space()
    {
        L.Install(() => "zh-CN", () => BppChineseLocaleMode.Mainland);
        var rewardId = Guid.Parse("30000000-0000-0000-0000-0000000000f1");
        var plan = Plan(
            level: 3,
            healthIncrease: 0,
            isRandomSelection: false,
            Group(new[] { rewardId }, limit: 1)
        );

        var text = LevelUpPreviewTextFormatter.Build(
            plan,
            _ => Step(rewardId, "闪亮!", "选择一件+10 生命的物品"),
            currentHero: null
        );

        Assert.Contains("<color=#FFD37E>闪亮!：</color>选择一件+ 10 生命的物品", text);
        Assert.DoesNotContain("： ", text);
    }

    [Fact]
    public void Build_keeps_hero_gated_groups_when_hero_is_unknown()
    {
        var poolIds = Enumerable
            .Range(0, 10)
            .Select(i => Guid.Parse($"40000000-0000-0000-0000-{i:d12}"))
            .ToList();
        var plan = Plan(
            level: 3,
            healthIncrease: 0,
            isRandomSelection: false,
            Group(poolIds, limit: 1, Heroes("Any", CardHero.Dooley))
        );

        var text = LevelUpPreviewTextFormatter.Build(plan, _ => null, currentHero: null);

        Assert.Contains("Random reward (10 options)", text);
    }

    [Fact]
    public void Random_selection_merges_uniform_weighted_groups_into_one_pool()
    {
        // Level 9/18 shape: dominant pool + enchant groups whose weight equals their
        // card count (w2 x [Yetarian, Sanguine] + w1 x [Arcane] = one random of 3).
        var poolIds = Enumerable
            .Range(0, 9)
            .Select(i => Guid.Parse($"50000000-0000-0000-0000-{i:d12}"))
            .ToList();
        var enchantA = Guid.Parse("60000000-0000-0000-0000-000000000001");
        var enchantB = Guid.Parse("60000000-0000-0000-0000-000000000002");
        var enchantC = Guid.Parse("60000000-0000-0000-0000-000000000003");
        var titles = new Dictionary<Guid, string>
        {
            [enchantA] = "Yetarian Tomb",
            [enchantB] = "Sanguine Valley",
            [enchantC] = "Arcane Abyss",
        };
        var plan = Plan(
            level: 9,
            healthIncrease: 0,
            isRandomSelection: true,
            WeightedGroup(poolIds, weight: 199),
            WeightedGroup(new[] { enchantA, enchantB }, weight: 2),
            WeightedGroup(new[] { enchantC }, weight: 1)
        );

        var text = LevelUpPreviewTextFormatter.Build(
            plan,
            id => titles.TryGetValue(id, out var title) ? Step(id, title) : null,
            currentHero: null
        );

        Assert.Contains("one of 3", text);
        Assert.Contains("Yetarian Tomb", text);
        Assert.Contains("Sanguine Valley", text);
        Assert.Contains("Arcane Abyss", text);
        Assert.DoesNotContain("one of 2", text);
        Assert.Contains("Random reward (9 options)", text);
    }

    [Fact]
    public void Duplicate_candidate_blocks_render_once()
    {
        // Level 7 shape: two weight-0 slots rolling over the same teacher pool.
        var poolIds = new[]
        {
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            Guid.Parse("70000000-0000-0000-0000-000000000002"),
        };
        var titles = new Dictionary<Guid, string>
        {
            [poolIds[0]] = "Bjorn",
            [poolIds[1]] = "Malafang",
        };
        var plan = Plan(
            level: 7,
            healthIncrease: 0,
            isRandomSelection: false,
            Group(poolIds, limit: 1),
            Group(poolIds, limit: 1)
        );

        var text = LevelUpPreviewTextFormatter.Build(
            plan,
            id => titles.TryGetValue(id, out var title) ? Step(id, title) : null,
            currentHero: null
        );

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "one of 2"));
    }

    [Fact]
    public void Pool_filtered_down_to_one_reward_renders_without_pool_header()
    {
        var mine = Guid.Parse("80000000-0000-0000-0000-000000000001");
        var otherHeros = Guid.Parse("80000000-0000-0000-0000-000000000002");
        var templates = new Dictionary<Guid, EncounterPreviewTemplatePlan>
        {
            [mine] = Step(mine, "Spare Change"),
            [otherHeros] = Step(otherHeros, "Core Initialization", null, CardHero.Dooley),
        };
        var plan = Plan(
            level: 3,
            healthIncrease: 0,
            isRandomSelection: false,
            Group(new[] { mine, otherHeros }, limit: 1)
        );

        var text = LevelUpPreviewTextFormatter.Build(
            plan,
            id => templates.TryGetValue(id, out var template) ? template : null,
            currentHero: CardHero.Jules
        );

        Assert.Contains("Spare Change", text);
        Assert.DoesNotContain("one of", text);
    }

    private static EncounterPreviewTemplatePlan Step(
        Guid id,
        string title,
        string? description = null,
        params CardHero[] heroes
    ) =>
        new(
            id,
            EncounterPreviewTemplateKind.EncounterStep,
            heroes,
            internalName: null,
            new EncounterPreviewLocalizedText(null, title),
            new EncounterPreviewLocalizedText(null, description ?? string.Empty),
            new Dictionary<string, EncounterPreviewAbilityValue>(),
            rewardFilter: null
        );

    private static LevelUpPreviewPlan Plan(
        int level,
        int healthIncrease,
        bool isRandomSelection,
        params LevelUpPreviewGroup[] groups
    ) => new(level, healthIncrease, isRandomSelection, groups);

    private static LevelUpPreviewHeroCondition Heroes(
        string comparisonOperator,
        params CardHero[] heroes
    ) => new(heroes, comparisonOperator);

    private static LevelUpPreviewGroup WeightedGroup(IReadOnlyCollection<Guid> ids, uint weight) =>
        new(weight, limit: 1, ids, heroConditions: null);

    private static LevelUpPreviewGroup Group(
        IReadOnlyCollection<Guid> ids,
        int limit,
        params LevelUpPreviewHeroCondition[] heroConditions
    ) => new(randomWeight: 0, limit, ids, heroConditions);
}
