using Xunit;

namespace EncounterTooltip.Tests;

public class EncounterRequirementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Outcome_query_pool_preserves_runtime_tier_behaviors(bool groupBehavior)
    {
        var behaviors = new BazaarGameShared.Domain.Spawning.SpawnBehaviors.ITSpawnBehavior[]
        {
            new BazaarGameShared.Domain.Spawning.SpawnBehaviors.TSpawnBehaviorTier
            {
                Tiers = new() { BazaarGameShared.Domain.Core.Types.ETier.Gold },
            },
            new BazaarGameShared.Domain.Spawning.SpawnBehaviors.TSpawnBehaviorInheritTier(),
        };
        var source = new
        {
            SelectionContext = new
            {
                SpawnContext = new
                {
                    SelectionMethod = "Random",
                    Behaviors = groupBehavior ? null : behaviors,
                    Groups = new[]
                    {
                        new
                        {
                            RandomWeight = 9,
                            Behaviors = groupBehavior ? behaviors : null,
                            Filters = new[]
                            {
                                new
                                {
                                    Constraints = new
                                    {
                                        Types = new[]
                                        {
                                            BazaarGameShared.Domain.Core.Types.ECardType.Item,
                                        },
                                        IsNot = false,
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        // The compiler prepares runtime objects before parsing the event groups.
        var token = EncounterStructuredParser.TryPrepareToken(source);
        Assert.True(EncounterStructuredParser.TryParseEventOutcomeGroups(token, out var groups));
        var reward = Assert.Single(Assert.Single(groups).QueryPools).Filter;
        Assert.NotNull(reward);
        Assert.Equal(new[] { BazaarGameShared.Domain.Core.Types.ETier.Gold }, reward.Tiers);
        Assert.False(reward.UsesDayTierTable);
        Assert.False(reward.UsesDayTierDistribution);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(10)]
    public void Treasure_chest_day_ranges_admit_only_one_monster_group(int day)
    {
        var token = Newtonsoft.Json.Linq.JObject.Parse(
            """
            { "SelectionContext": { "SpawnContext": {
              "SelectionMethod": "Random", "Groups": []
            } } }
            """
        );
        var groupsToken = (Newtonsoft.Json.Linq.JArray)
            token.SelectToken("SelectionContext.SpawnContext.Groups")!;
        // GameData treasure chest monster brackets: [2,4), [4,7), [7,10), [10,infinity).
        foreach (var (lower, upper) in new[] { (2, 4), (4, 7), (7, 10), (10, int.MaxValue) })
        {
            groupsToken.Add(
                Newtonsoft.Json.Linq.JObject.FromObject(
                    new
                    {
                        RandomWeight = 1,
                        Filters = new[] { new { Ids = new[] { Guid.NewGuid() } } },
                        Prerequisites = new[]
                        {
                            new
                            {
                                Conditions = new
                                {
                                    CurrentDay = lower,
                                    ComparisonOperator = "GreaterThanOrEqual",
                                },
                            },
                            new
                            {
                                Conditions = new
                                {
                                    CurrentDay = upper,
                                    ComparisonOperator = "LessThan",
                                },
                            },
                        },
                    }
                )
            );
        }

        Assert.True(EncounterStructuredParser.TryParseEventOutcomeGroups(token, out var groups));
        var active = Assert.Single(groups, group => group.DayCondition!.Value.Matches(day));
        Assert.Equal(1u, active.Weight);
    }

    private static readonly Guid PackageId = Guid.Parse("b0000000-0000-0000-0000-000000000001");

    // Farai's delivery outcomes: the group only rolls while you do NOT own the
    // package ("Equal 0") — the exact inverse of a plain has-check.
    [Fact]
    public void Equal_zero_requirement_is_met_only_without_the_card()
    {
        var requirement = new EncounterCardRequirement(
            new[] { PackageId },
            Array.Empty<IReadOnlyList<string>>(),
            tagOperator: "Any",
            comparison: "Equal",
            amount: 0
        );

        Assert.True(requirement.Matches(Inventory()));
        Assert.False(requirement.Matches(Inventory(Card(PackageId))));
    }

    [Fact]
    public void Count_threshold_requirements_count_duplicates()
    {
        var requirement = new EncounterCardRequirement(
            new[] { PackageId },
            Array.Empty<IReadOnlyList<string>>(),
            tagOperator: "Any",
            comparison: "GreaterThanOrEqual",
            amount: 2
        );

        Assert.False(requirement.Matches(Inventory(Card(PackageId))));
        Assert.True(requirement.Matches(Inventory(Card(PackageId), Card(PackageId))));
    }

    [Fact]
    public void Tag_requirement_with_equal_zero_inverts_on_hidden_tag_ownership()
    {
        var requirement = new EncounterCardRequirement(
            Array.Empty<Guid>(),
            new IReadOnlyList<string>[] { new[] { "Package" } },
            tagOperator: "Any",
            comparison: "Equal",
            amount: 0
        );

        Assert.True(requirement.Matches(Inventory(Card(Guid.NewGuid(), "Food"))));
        Assert.False(requirement.Matches(Inventory(Card(Guid.NewGuid(), "Package"))));
    }

    // Treasure Chest: weight-1 fixed id + weight-9 TSpawnFilterQuery pool. Dropping
    // the query group used to show the fixed chest as 100%.
    [Fact]
    public void Outcome_groups_keep_query_pools_in_the_roll()
    {
        var json = """
            {
              "$type": "TCardEncounterEvent",
              "SelectionContext": {
                "SpawnContext": {
                  "SelectionMethod": "Random",
                  "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                  "Groups": [
                    {
                      "RandomWeight": 1,
                      "Filters": [
                        { "$type": "TSpawnFilterIdList", "Ids": ["10000000-0000-0000-0000-000000000001"] }
                      ]
                    },
                    {
                      "RandomWeight": 9,
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Filters": [
                        {
                          "$type": "TSpawnFilterQuery",
                          "Constraints": {
                            "$type": "ConstraintAnd",
                            "Constraints": [
                              { "$type": "ConstraintSize", "Sizes": ["Small", "Medium"], "IsNot": false },
                              { "$type": "ConstraintCardType", "Types": ["Item"], "IsNot": false },
                              { "$type": "ConstraintTag", "Tags": ["Loot"], "IsNot": true }
                            ]
                          }
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;

        Assert.True(EncounterStructuredParser.TryParseEventOutcomeGroups(json, out var groups));

        Assert.Equal(2, groups.Count);
        Assert.Equal(1u, groups[0].Weight);
        Assert.Empty(groups[0].QueryPools);
        Assert.Equal(9u, groups[1].Weight);
        Assert.Empty(groups[1].Ids);
        var pool = Assert.Single(groups[1].QueryPools);
        Assert.NotNull(pool.Filter);
    }

    // Runtime templates lose $type and serialize enum tags numerically; a numeric
    // Comparison must still parse ("2" = GreaterThanOrEqual is positional — verify
    // via names to stay robust against enum reordering).
    [Fact]
    public void Requirements_parse_from_prerequisites_without_type_markers()
    {
        var json = """
            [
              {
                "Subject": {
                  "Conditions": { "Id": "b0000000-0000-0000-0000-000000000001", "IsNot": false }
                },
                "Comparison": "Equal",
                "Amount": 0
              }
            ]
            """;

        var requirements = EncounterStructuredParser.ReadCardRequirements(
            Newtonsoft.Json.Linq.JToken.Parse(json)
        );

        var requirement = Assert.Single(requirements);
        Assert.Equal(new[] { PackageId }, requirement.Ids);
        Assert.Equal("Equal", requirement.Comparison);
        Assert.Equal(0, requirement.Amount);
    }

    [Fact]
    public void Negated_and_tier_conditionals_yield_no_requirement()
    {
        var json = """
            [
              {
                "Subject": {
                  "Conditions": { "Id": "b0000000-0000-0000-0000-000000000001", "IsNot": true }
                },
                "Comparison": "Equal",
                "Amount": 0
              },
              {
                "Subject": {
                  "Conditions": { "$type": "TCardConditionalTier", "Tiers": ["Bronze"], "IsNot": false }
                },
                "Comparison": "Equal",
                "Amount": 0
              }
            ]
            """;

        Assert.Empty(
            EncounterStructuredParser.ReadCardRequirements(Newtonsoft.Json.Linq.JToken.Parse(json))
        );
    }

    private static EncounterInventory Inventory(params EncounterInventoryCard[] cards) =>
        new(cards.ToList());

    private static EncounterInventoryCard Card(Guid templateId, params string[] tags) =>
        new(templateId, new HashSet<string>(tags, StringComparer.Ordinal));
}
