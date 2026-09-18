using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Encounter.Step;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Game;
using BazaarGameShared.Domain.Prerequisites;
using BazaarGameShared.Domain.Prerequisites.Conditionals;
using BazaarGameShared.Domain.Spawning;
using BazaarGameShared.Domain.Spawning.SpawnFilters;
using BazaarGameShared.Domain.Spawning.SpawnGroups;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarGameShared.Domain.Values;
using BazaarPlusPlus.Localization;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class LevelUpPreviewTextFormatterTests
{
    public LevelUpPreviewTextFormatterTests()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
    }

    [Fact]
    public void Build_renders_health_pools_and_hero_filtered_groups()
    {
        var poolIds = Enumerable
            .Range(0, 25)
            .Select(i => Guid.Parse($"20000000-0000-0000-0000-{i:d12}"))
            .ToList();
        var levelUp = new TLevelUp
        {
            Level = 2,
            HealthIncrease = 150,
            Rewards = new TSpawnContextQuery
            {
                Groups =
                {
                    // Hero-excluded group (current hero Vanessa is not Dooley/Jules).
                    Group(
                        new[] { Guid.Parse("30000000-0000-0000-0000-000000000001") },
                        limit: 1,
                        new TPrerequisiteRun
                        {
                            Conditions = new TRunConditionalPlayerHero
                            {
                                Heroes = { EHero.Dooley, EHero.Jules },
                                Operator = EListComparisonOperator.None,
                            },
                        }
                    ),
                    // Hero-mismatched group (requires Dooley/Jules).
                    Group(
                        new[] { Guid.Parse("30000000-0000-0000-0000-000000000002") },
                        limit: 1,
                        new TPrerequisiteRun
                        {
                            Conditions = new TRunConditionalPlayerHero
                            {
                                Heroes = { EHero.Dooley, EHero.Jules },
                                Operator = EListComparisonOperator.Any,
                            },
                        }
                    ),
                    // Random pool.
                    Group(poolIds, limit: 3),
                    // Board-conditional bonus -> omitted.
                    Group(
                        new[] { Guid.Parse("30000000-0000-0000-0000-000000000003") },
                        limit: 1,
                        new TPrerequisiteCardCount()
                    ),
                },
            },
        };

        var text = LevelUpPreviewTextFormatter.Build(
            Plan(levelUp),
            resolveTemplate: _ => null,
            currentHero: EHero.Vanessa,
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
            LevelUpPreviewTextFormatter.Build(null, _ => null, EHero.Vanessa)
        );
    }

    [Fact]
    public void Build_uses_live_values_for_dynamic_reward_totals()
    {
        var rewardId = Guid.Parse("30000000-0000-0000-0000-0000000000d1");
        var levelUp = new TLevelUp
        {
            Level = 3,
            Rewards = new TSpawnContextQuery { Groups = { Group(new[] { rewardId }, limit: 1) } },
        };
        var reward = new EncounterPreviewTemplatePlan(
            rewardId,
            EncounterPreviewTemplateKind.EncounterStep,
            Array.Empty<EHero>(),
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
            Plan(levelUp),
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
        L.Install(new TestLanguageProvider("zh-CN"), new TestLocaleModeProvider());
        var rewardId = Guid.Parse("30000000-0000-0000-0000-0000000000f1");
        var levelUp = new TLevelUp
        {
            Level = 3,
            Rewards = new TSpawnContextQuery { Groups = { Group(new[] { rewardId }, limit: 1) } },
        };

        var text = LevelUpPreviewTextFormatter.Build(
            Plan(levelUp),
            _ => Preview(Step("闪亮!", "选择一件+10 生命的物品")),
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
        var levelUp = new TLevelUp
        {
            Level = 3,
            Rewards = new TSpawnContextQuery
            {
                Groups =
                {
                    Group(
                        poolIds,
                        limit: 1,
                        new TPrerequisiteRun
                        {
                            Conditions = new TRunConditionalPlayerHero
                            {
                                Heroes = { EHero.Dooley },
                                Operator = EListComparisonOperator.Any,
                            },
                        }
                    ),
                },
            },
        };

        var text = LevelUpPreviewTextFormatter.Build(Plan(levelUp), _ => null, currentHero: null);

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
        var levelUp = new TLevelUp
        {
            Level = 9,
            Rewards = new TSpawnContextQuery
            {
                SelectionMethod = ESpawnSelectionMethod.Random,
                Groups =
                {
                    WeightedGroup(poolIds, weight: 199),
                    WeightedGroup(new List<Guid> { enchantA, enchantB }, weight: 2),
                    WeightedGroup(new List<Guid> { enchantC }, weight: 1),
                },
            },
        };

        var text = LevelUpPreviewTextFormatter.Build(
            Plan(levelUp),
            id => titles.TryGetValue(id, out var title) ? Preview(Step(title)) : null,
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
        var poolIds = new List<Guid>
        {
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            Guid.Parse("70000000-0000-0000-0000-000000000002"),
        };
        var titles = new Dictionary<Guid, string>
        {
            [poolIds[0]] = "Bjorn",
            [poolIds[1]] = "Malafang",
        };
        var levelUp = new TLevelUp
        {
            Level = 7,
            Rewards = new TSpawnContextQuery
            {
                Groups = { Group(poolIds, limit: 1), Group(poolIds, limit: 1) },
            },
        };

        var text = LevelUpPreviewTextFormatter.Build(
            Plan(levelUp),
            id => titles.TryGetValue(id, out var title) ? Preview(Step(title)) : null,
            currentHero: null
        );

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "one of 2"));
    }

    [Fact]
    public void Pool_filtered_down_to_one_reward_renders_without_pool_header()
    {
        var mine = Guid.Parse("80000000-0000-0000-0000-000000000001");
        var otherHeros = Guid.Parse("80000000-0000-0000-0000-000000000002");
        var templates = new Dictionary<Guid, TCardEncounterStep>
        {
            [mine] = Step("Spare Change"),
            [otherHeros] = Step("Core Initialization"),
        };
        templates[otherHeros].Heroes.Add(EHero.Dooley);
        var levelUp = new TLevelUp
        {
            Level = 3,
            Rewards = new TSpawnContextQuery
            {
                Groups = { Group(new List<Guid> { mine, otherHeros }, limit: 1) },
            },
        };

        var text = LevelUpPreviewTextFormatter.Build(
            Plan(levelUp),
            id => templates.TryGetValue(id, out var template) ? Preview(template) : null,
            currentHero: EHero.Jules
        );

        Assert.Contains("Spare Change", text);
        Assert.DoesNotContain("one of", text);
    }

    private static TCardEncounterStep Step(string title, string? description = null) =>
        new()
        {
            Localization = new TCardLocalization
            {
                Title = new TLocalizableText { Text = title },
                Description = new TLocalizableText { Text = description ?? string.Empty },
            },
        };

    private static LevelUpPreviewPlan Plan(TLevelUp levelUp)
    {
        var query = Assert.IsType<TSpawnContextQuery>(levelUp.Rewards);
        var groups = new List<LevelUpPreviewGroup>();
        foreach (var group in query.Groups)
        {
            if (
                group.Prerequisites?.Any(prerequisite => prerequisite is TPrerequisiteCardCount)
                == true
            )
                continue;

            var ids = group
                .Filters.OfType<TSpawnFilterIdList>()
                .SelectMany(filter => filter.Ids)
                .ToArray();
            var heroConditions = group
                .Prerequisites?.OfType<TPrerequisiteRun>()
                .Select(prerequisite => prerequisite.Conditions)
                .OfType<TRunConditionalPlayerHero>()
                .Select(condition => new LevelUpPreviewHeroCondition(
                    condition.Heroes,
                    condition.Operator.ToString()
                ))
                .ToArray();
            groups.Add(
                new LevelUpPreviewGroup(
                    group.RandomWeight,
                    group.Limit is TFixedValue fixedValue ? (int)fixedValue.Value : 1,
                    ids,
                    heroConditions
                )
            );
        }
        return new LevelUpPreviewPlan(
            (int)levelUp.Level,
            (int)levelUp.HealthIncrease,
            query.SelectionMethod == ESpawnSelectionMethod.Random,
            groups
        );
    }

    private static EncounterPreviewTemplatePlan Preview(TCardBase template) =>
        new(
            template.Id,
            EncounterPreviewTemplateKind.EncounterStep,
            template.Heroes,
            template.InternalName,
            new EncounterPreviewLocalizedText(
                template.Localization?.Title?.Key,
                template.Localization?.Title?.Text
            ),
            new EncounterPreviewLocalizedText(
                template.Localization?.Description?.Key,
                template.Localization?.Description?.Text
            ),
            new Dictionary<string, EncounterPreviewAbilityValue>(),
            rewardFilter: null
        );

    private static TSpawnGroup WeightedGroup(List<Guid> ids, uint weight) =>
        new() { Filters = { new TSpawnFilterIdList { Ids = ids } }, RandomWeight = weight };

    private static TSpawnGroup Group(
        IEnumerable<Guid> ids,
        int limit,
        params BazaarGameShared.Domain.Prerequisites.ITPrerequisite[] prerequisites
    ) =>
        new()
        {
            Filters = { new TSpawnFilterIdList { Ids = ids.ToList() } },
            Limit = new TFixedValue { Value = limit },
            Prerequisites = prerequisites.Length == 0 ? null : prerequisites.ToList(),
        };

    private sealed class TestLanguageProvider(string languageCode = "en") : ILanguageProvider
    {
        public string CurrentLanguageCode { get; } = languageCode;
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        public BppChineseLocaleMode CurrentMode => BppChineseLocaleMode.Mainland;
    }
}
