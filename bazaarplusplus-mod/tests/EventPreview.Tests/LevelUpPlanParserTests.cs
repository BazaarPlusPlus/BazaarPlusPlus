using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Localization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EventPreview.Tests;

/// <summary>
/// A static-data <c>level_ups</c> row read off its JSON, the Android host's input, held to the
/// rules of the desktop's typed compiler (CollectionEncounterTooltip.Tests'
/// <c>LevelUpPreviewCompilerTests</c>), then rendered by the formatter both hosts share.
/// </summary>
public sealed class LevelUpPlanParserTests
{
    private static readonly Guid Ruby = Guid.Parse("1453be13-1d81-45ea-82a5-7d1ef35ec78b");
    private static readonly Guid Jules = Guid.Parse("1596d1df-e943-48b4-bfc6-df2edb9dfe55");
    private static readonly Guid PoolA = Guid.Parse("950eb616-0921-4024-a4a6-a03c4f3f8135");
    private static readonly Guid PoolB = Guid.Parse("50c95e56-67fa-4ef9-b795-df089de6d490");
    private static readonly Guid Rage = Guid.Parse("0c71aef1-c049-483a-b9a0-19c5005b4fc7");
    private static readonly Guid Note = Guid.Parse("74d5ddf6-3654-45e7-8859-483bd8c84ff7");

    // Raw JSON carries enum names, so the host's normalization has nothing to add.
    private static readonly EncounterChoicePlanParser Parser = new(
        (_, _) => null,
        (_, _, _, _) => null
    );

    public LevelUpPlanParserTests()
    {
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);
    }

    // A trimmed copy of the game's level 2 row: the outer query is Sequential over Random groups.
    private static readonly string LevelTwo = $$"""
        {
          "$type": "TLevelUp",
          "Level": 2,
          "Rewards": {
            "$type": "TSpawnContextQuery",
            "Groups": [
              {
                "$type": "TSpawnGroup",
                "Filters": [{ "$type": "TSpawnFilterIdList", "Ids": ["{{Ruby}}"] }],
                "SelectionMethod": "Random",
                "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                "Prerequisites": [{
                  "$type": "TPrerequisiteRun",
                  "Conditions": { "$type": "TRunConditionalPlayerHero", "Heroes": ["Dooley", "Jules", "Hero8"], "Operator": "None" }
                }],
                "RandomWeight": 0
              },
              {
                "$type": "TSpawnGroup",
                "Filters": [{ "$type": "TSpawnFilterIdList", "Ids": ["{{Jules}}"] }],
                "SelectionMethod": "Random",
                "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                "Prerequisites": [{
                  "$type": "TPrerequisiteRun",
                  "Conditions": { "$type": "TRunConditionalPlayerHero", "Heroes": ["Dooley", "Jules"], "Operator": "Any" }
                }],
                "RandomWeight": 0
              },
              {
                "$type": "TSpawnGroup",
                "Filters": [
                  { "$type": "TSpawnFilterIdList", "Ids": ["{{PoolA}}", "{{PoolB}}"] },
                  { "$type": "TSpawnFilterQuery", "Constraints": {} }
                ],
                "SelectionMethod": "Random",
                "Limit": { "$type": "TFixedValue", "Value": 3.0 },
                "Prerequisites": null,
                "RandomWeight": 0
              },
              {
                "$type": "TSpawnGroup",
                "Filters": [{ "$type": "TSpawnFilterIdList", "Ids": ["{{Rage}}"] }],
                "SelectionMethod": "Random",
                "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                "Prerequisites": [
                  { "$type": "TPrerequisiteCardCount", "Comparison": "GreaterThanOrEqual", "Amount": 1 },
                  { "$type": "TPrerequisiteRun", "Conditions": { "$type": "TRunConditionalOr", "Conditions": [] } }
                ],
                "RandomWeight": 22
              },
              {
                "$type": "TSpawnGroup",
                "Filters": [{ "$type": "TSpawnFilterIdList", "Ids": ["{{Note}}"] }],
                "SelectionMethod": "Random",
                "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                "Prerequisites": [
                  { "$type": "TPrerequisiteRun", "Conditions": { "$type": "TRunConditionalOr", "Conditions": [] } }
                ],
                "RandomWeight": 22
              },
              {
                "$type": "TSpawnGroup",
                "Filters": [{ "$type": "TSpawnFilterQuery", "Constraints": {} }],
                "SelectionMethod": "Random",
                "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                "RandomWeight": 0
              }
            ],
            "SelectionMethod": "Sequential",
            "Limit": { "$type": "TFixedValue", "Value": 4.0 }
          },
          "TutorialRewards": { "$type": "TSpawnContextQuery", "Groups": [] },
          "HealthIncrease": 150,
          "Version": "5.0.0"
        }
        """;

    private static LevelUpPreviewPlan? Parse(int key, string json) =>
        Parser.TryParseLevelUp(
            key,
            JObject.Parse(json),
            name => Enum.TryParse<CardHero>(name, out var hero) ? hero : null
        );

    [Fact]
    public void Reads_a_level_up_row_as_the_desktop_compiler_reads_the_typed_level_up()
    {
        var plan = Parse(2, LevelTwo);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Level);
        Assert.Equal(150, plan.HealthIncrease);
        // The outer query decides, not its groups.
        Assert.False(plan.IsRandomSelection);
        // The board-gated group is left out; the empty query-only group adds nothing.
        Assert.Equal(
            new[] { new[] { Ruby }, new[] { Jules }, new[] { PoolA, PoolB }, new[] { Note } },
            plan.Groups.Select(group => group.TemplateIds.ToArray())
        );
        var excluded = Assert.Single(plan.Groups[0].HeroConditions);
        Assert.Equal("None", excluded.ComparisonOperator);
        // A native hero with no facet drops out of the condition.
        Assert.Equal(new[] { CardHero.Dooley, CardHero.Jules }, excluded.Heroes);
        Assert.Equal("Any", Assert.Single(plan.Groups[1].HeroConditions).ComparisonOperator);
        Assert.Equal(3, plan.Groups[2].Limit);
        // Any run condition other than a hero one is ignored, not a reason to drop the group.
        Assert.Empty(plan.Groups[3].HeroConditions);
        Assert.Equal(22u, plan.Groups[3].RandomWeight);
    }

    private static readonly Guid Yetarian = Guid.Parse("04f879e5-94fe-42ae-aa7a-273edbd6d410");
    private static readonly Guid Sanguine = Guid.Parse("8dead654-6cd3-4819-a0f1-b9d5a70b59f7");
    private static readonly Guid Arcane = Guid.Parse("f1878b7e-cad6-4301-a5c1-bccf5d35da7b");

    // A trimmed copy of the game's level 9 row (Behaviors dropped): the outer query is Random
    // over a pool of nine pedestals weighted 199 and two enchant groups weighted by their card
    // count, each group with a null Limit.
    private static readonly string LevelNine = $$"""
        {
          "$type": "TLevelUp",
          "Level": 9,
          "Rewards": {
            "$type": "TSpawnContextQuery",
            "Groups": [
              {
                "$type": "TSpawnGroup",
                "Filters": [{
                  "$type": "TSpawnFilterIdList",
                  "Ids": [
                    "4df2e5f8-1a7f-4046-b374-8f1642b24751", "5f65bbe1-bf39-4fe7-8eeb-57b669df1ee6",
                    "9c1f2654-3077-4c4e-b38e-946aed17cbfb", "c1e69f94-fa5a-4d96-bb3e-1a4a0c8b4b04",
                    "47c256d3-6f3e-495b-b119-12739adda129", "320e7f80-334b-440c-8cc1-0b54e3141335",
                    "c3925fd2-d174-40ff-8d15-3d9c41b3615b", "81110f4a-ab1e-4a9e-af1f-a0a4ce44b629",
                    "41e6d3fc-2795-4107-ba0a-29ecad00f37b"
                  ]
                }],
                "SelectionMethod": "Random",
                "Limit": null,
                "Prerequisites": null,
                "RandomWeight": 199
              },
              {
                "$type": "TSpawnGroup",
                "Filters": [{ "$type": "TSpawnFilterIdList", "Ids": ["{{Yetarian}}", "{{Sanguine}}"] }],
                "SelectionMethod": "Random",
                "Limit": null,
                "Prerequisites": null,
                "RandomWeight": 2
              },
              {
                "$type": "TSpawnGroup",
                "Filters": [{ "$type": "TSpawnFilterIdList", "Ids": ["{{Arcane}}"] }],
                "SelectionMethod": "Random",
                "Limit": null,
                "Prerequisites": null,
                "RandomWeight": 1
              }
            ],
            "SelectionMethod": "Random",
            "Limit": { "$type": "TFixedValue", "Value": 2.0 }
          },
          "TutorialRewards": null,
          "HealthIncrease": 600,
          "Version": "5.0.0"
        }
        """;

    [Fact]
    public void Reads_random_selection_off_the_outer_query_and_renders_the_desktop_text()
    {
        var plan = Parse(9, LevelNine);

        Assert.NotNull(plan);
        Assert.True(plan!.IsRandomSelection);
        Assert.Equal(600, plan.HealthIncrease);
        Assert.Equal(new uint[] { 199, 2, 1 }, plan.Groups.Select(group => group.RandomWeight));
        // A null Limit is one draw.
        Assert.All(plan.Groups, group => Assert.Equal(1, group.Limit));
        Assert.Equal(new[] { Yetarian, Sanguine }, plan.Groups[1].TemplateIds);

        var templates = new Dictionary<Guid, EncounterPreviewTemplatePlan>
        {
            [Yetarian] = Template(Yetarian, "Yetarian Tomb", "Enchant an item with Icy."),
            [Sanguine] = Template(Sanguine, "Sanguine Valley", "Enchant an item with Obsidian."),
            [Arcane] = Template(Arcane, "Arcane Abyss", "Enchant an item with Shiny."),
        };
        var text = LevelUpPreviewTextFormatter.Build(
            plan,
            id => templates.TryGetValue(id, out var template) ? template : null,
            CardHero.Vanessa,
            currentLevel: 9
        );

        Assert.Contains("+ 600 Max Health", text);
        Assert.DoesNotContain("board slots", text);
        Assert.Contains("Choose one:", text);
        Assert.Contains("Random reward (9 options)", text);
        // The two enchant groups, weighted by their card count, are one pool of three.
        Assert.Contains("one of 3", text);
        Assert.Contains("Yetarian Tomb", text);
        Assert.Contains("Sanguine Valley", text);
        Assert.Contains("Arcane Abyss", text);
    }

    [Fact]
    public void Rejects_a_row_whose_key_disagrees_with_its_level()
    {
        Assert.Null(Parse(3, LevelTwo));
    }

    [Fact]
    public void The_parsed_row_renders_the_desktop_text_for_the_current_hero()
    {
        var plan = Parse(2, LevelTwo);
        var templates = new Dictionary<Guid, EncounterPreviewTemplatePlan>
        {
            [Ruby] = Template(Ruby, "Ruby", "Gain a Ruby."),
            [Jules] = Template(Jules, "Recipe Book", "Learn a recipe."),
            [Note] = Template(Note, "Melody", "Play a note."),
        };

        var text = LevelUpPreviewTextFormatter.Build(
            plan,
            id => templates.TryGetValue(id, out var template) ? template : null,
            CardHero.Vanessa,
            currentLevel: 2
        );

        Assert.Contains("+ 150 Max Health", text);
        Assert.Contains("+ 2 board slots", text);
        Assert.Contains("Ruby", text);
        Assert.Contains("Melody", text);
        // Jules's group requires Dooley or Jules.
        Assert.DoesNotContain("Recipe Book", text);
    }

    private static EncounterPreviewTemplatePlan Template(
        Guid id,
        string title,
        string description
    ) =>
        new(
            id,
            EncounterPreviewTemplateKind.Item,
            heroes: null,
            title,
            new EncounterPreviewLocalizedText(null, title),
            new EncounterPreviewLocalizedText(null, description),
            new Dictionary<string, EncounterPreviewAbilityValue>(),
            rewardFilter: null
        );
}
