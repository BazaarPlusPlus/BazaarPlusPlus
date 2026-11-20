using BazaarPlusPlus.Core.Cards;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterStructuredParserTests
{
    [Fact]
    public void TryParseEventStepReferences_reads_step_ids_from_spawn_group_filters()
    {
        var brewId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var tradeId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var tinyFurryMonsterId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var json = """
            {
              "$type": "TCardEncounterEvent",
              "SelectionContext": {
                "SpawnContext": {
                  "$type": "TSpawnContextQuery",
                  "Groups": [
                    {
                      "Filters": [
                        {
                          "Ids": [
                            "10000000-0000-0000-0000-000000000001",
                            "10000000-0000-0000-0000-000000000002"
                          ],
                          "Prerequisites": {
                            "$type": "TPrerequisiteCardCount",
                            "Subject": {
                              "Conditions": {
                                "$type": "TCardConditionalId",
                                "Id": "20000000-0000-0000-0000-000000000001",
                                "IsNot": false
                              }
                            },
                            "Comparison": "GreaterThanOrEqual",
                            "Amount": 1
                          }
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;

        var references = ParseStepReferences(json);

        Assert.Equal(
            new[] { brewId, tradeId },
            references.Select(reference => reference.TemplateId)
        );
        Assert.Equal(new[] { tinyFurryMonsterId }, Assert.Single(references[0].Requirements).Ids);
        Assert.Equal(new[] { tinyFurryMonsterId }, Assert.Single(references[1].Requirements).Ids);
    }

    [Fact]
    public void TryParseEventStepReferences_combines_group_and_filter_prerequisite_ids()
    {
        var stepId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var groupPrerequisiteId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var filterPrerequisiteId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var json = """
            {
              "$type": "TCardEncounterEvent",
              "SelectionContext": {
                "SpawnContext": {
                  "Groups": [
                    {
                      "Prerequisites": {
                        "$type": "TPrerequisiteCardCount",
                        "Subject": {
                          "Conditions": {
                            "$type": "TCardConditionalId",
                            "Id": "20000000-0000-0000-0000-000000000002",
                            "IsNot": false
                          }
                        },
                        "Comparison": "GreaterThanOrEqual",
                        "Amount": 1
                      },
                      "Filters": [
                        {
                          "Ids": ["10000000-0000-0000-0000-000000000003"],
                          "Prerequisites": {
                            "$type": "TPrerequisiteCardCount",
                            "Subject": {
                              "Conditions": {
                                "$type": "TCardConditionalId",
                                "Id": "20000000-0000-0000-0000-000000000003",
                                "IsNot": false
                              }
                            },
                            "Comparison": "GreaterThanOrEqual",
                            "Amount": 1
                          }
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;

        var reference = Assert.Single(ParseStepReferences(json));

        Assert.Equal(stepId, reference.TemplateId);
        Assert.Equal(2, reference.Requirements.Count);
        Assert.Equal(new[] { groupPrerequisiteId }, reference.Requirements[0].Ids);
        Assert.Equal(new[] { filterPrerequisiteId }, reference.Requirements[1].Ids);
    }

    [Fact]
    public void TryParseRewardFilter_reads_deal_card_constraints_from_action_spawn_context()
    {
        var json = """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                {
                                  "$type": "ConstraintCardType",
                                  "Types": ["Item"],
                                  "IsNot": false
                                },
                                {
                                  "$type": "ConstraintSize",
                                  "Sizes": ["Medium"],
                                  "IsNot": false
                                },
                                {
                                  "$type": "ConstraintTier",
                                  "Tiers": ["Bronze", "Silver", "Gold", "Diamond"],
                                  "IsNot": false
                                },
                                {
                                  "$type": "ConstraintTag",
                                  "Tags": ["Loot"],
                                  "IsNot": true
                                }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = ParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(CardType.Item, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(new[] { CardSize.Medium }, reward.Sizes);
        Assert.Equal(
            new[] { CardTier.Bronze, CardTier.Silver, CardTier.Gold, CardTier.Diamond },
            reward.Tiers
        );
        Assert.Empty(reward.Tags);
        Assert.Equal(new[] { CardTag.Loot }, reward.ExcludedTags);
        Assert.Equal("Medium Bronze-Diamond Item, not Loot", reward.FilterSummary);
    }

    [Fact]
    public void TryParseRewardFilter_maps_excluded_tier_and_size_to_included_complements()
    {
        var json = """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "Limit": { "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Item"] },
                                { "$type": "ConstraintTag", "Tags": ["Tool"] },
                                { "$type": "ConstraintTier", "Tiers": ["Legendary"], "IsNot": true },
                                { "$type": "ConstraintSize", "Sizes": ["Large"], "IsNot": true }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = ParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(CardType.Item, reward.CardType);
        Assert.Equal(new[] { CardSize.Small, CardSize.Medium }, reward.Sizes);
        Assert.Equal(
            new[] { CardTier.Bronze, CardTier.Silver, CardTier.Gold, CardTier.Diamond },
            reward.Tiers
        );
        Assert.Equal(new[] { CardTag.Tool }, reward.Tags);
        Assert.Equal("Small Medium Bronze-Diamond Tool", reward.FilterSummary);
    }

    [Fact]
    public void TryParseRewardFilter_reads_only_tier_fields_from_spawn_behavior_tier()
    {
        var json = """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Item"] },
                                { "$type": "ConstraintTag", "Tags": ["Food"] }
                              ]
                            }
                          ]
                        }
                      ],
                      "Behaviors": [
                        {
                          "$type": "TSpawnBehaviorTier",
                          "Tiers": ["Diamond"],
                          "DisplayName": "Gold"
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = ParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(new[] { CardTier.Diamond }, reward.Tiers);
    }

    [Fact]
    public void TryParseRewardFilter_marks_ignore_tier_table_rewards_as_not_day_tier_driven()
    {
        var json = """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": {
                                "$type": "ConstraintAnd",
                                "Constraints": [
                                  { "$type": "ConstraintCardType", "Types": ["Item"], "IsNot": false },
                                  { "$type": "ConstraintTag", "Tags": ["Weapon"], "IsNot": false }
                                ]
                              }
                            }
                          ]
                        }
                      ],
                      "Behaviors": [
                        { "$type": "TSpawnBehaviorIgnoreTierTable", "IgnoreTierTable": true }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = ParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(new[] { CardTag.Weapon }, reward.Tags);
        Assert.False(reward.UsesDayTierTable);
        Assert.False(reward.UsesDayTierDistribution);
    }

    [Fact]
    public void TryParseRewardFilter_reads_token_constraints_without_type_metadata()
    {
        var json = """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                { "Types": ["Item"] },
                                { "Sizes": ["Small"] },
                                { "Tiers": ["Silver"] },
                                { "Tags": ["Food"] }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = ParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(CardType.Item, reward.CardType);
        Assert.Equal(new[] { CardSize.Small }, reward.Sizes);
        Assert.Equal(new[] { CardTier.Silver }, reward.Tiers);
        Assert.Equal(new[] { CardTag.Food }, reward.Tags);
    }

    [Fact]
    public void TryParseRewardFilter_merges_alternative_filters_of_the_same_card_type()
    {
        var json = """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "Limit": { "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Item"] },
                                { "$type": "ConstraintTag", "Tags": ["Tool"] },
                                { "$type": "ConstraintSize", "Sizes": ["Small"] }
                              ]
                            },
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Item"] },
                                { "$type": "ConstraintTag", "Tags": ["Weapon"] },
                                { "$type": "ConstraintSize", "Sizes": ["Large"] }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = ParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(CardType.Item, reward!.CardType);
        Assert.Equal(new[] { CardSize.Small, CardSize.Large }, reward.Sizes);
        Assert.Equal(new[] { CardTag.Tool, CardTag.Weapon }, reward.Tags);
    }

    [Fact]
    public void TryParseRewardFilter_returns_null_for_alternative_filters_of_different_card_types()
    {
        var json = """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "Limit": { "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Item"] },
                                { "$type": "ConstraintSize", "Sizes": ["Small"] }
                              ]
                            },
                            {
                              "Constraints": [
                                { "$type": "ConstraintCardType", "Types": ["Skill"] }
                              ]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = ParseRewardFilter(json);

        Assert.Null(reward);
    }

    [Fact]
    public void TryParseRewardFilter_skips_unconstrained_deal_card_actions()
    {
        var json = """
            {
              "$type": "TCardEncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 }
                    }
                  }
                },
                "1": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Types": ["Skill"],
                              "HiddenTags": ["Freeze"]
                            }
                          ]
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = ParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(CardType.Skill, reward.CardType);
        Assert.Equal(new[] { CardHiddenTag.Freeze }, reward.Keywords);
    }

    [Fact]
    public void TryParseEventStepReferences_reads_tag_prerequisite_groups()
    {
        var json = """
            {
              "$type": "TCardEncounterEvent",
              "SelectionContext": {
                "SpawnContext": {
                  "Limit": { "$type": "TFixedValue", "Value": 3.0 },
                  "Groups": [
                    {
                      "Filters": [{ "Ids": ["10000000-0000-0000-0000-000000000001"] }],
                      "Prerequisites": [
                        {
                          "$type": "TPrerequisiteCardCount",
                          "Subject": {
                            "$type": "TTargetCardSection",
                            "TargetSection": "AbsolutePlayerHandAndStash",
                            "Conditions": {
                              "$type": "TCardConditionalTag",
                              "Tags": ["Toy", "Drone"],
                              "Operator": "Any"
                            }
                          },
                          "Comparison": "GreaterThanOrEqual",
                          "Amount": 1
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;

        var references = ParseStepReferences(json);

        var reference = Assert.Single(references);
        var requirement = Assert.Single(reference.Requirements);
        Assert.Equal(2, requirement.TagCandidateGroups.Count);
        Assert.Equal(new[] { "Toy" }, requirement.TagCandidateGroups[0]);
        Assert.Equal(new[] { "Drone" }, requirement.TagCandidateGroups[1]);
        Assert.Equal("Any", requirement.TagOperator);
        Assert.Equal("GreaterThanOrEqual", requirement.Comparison);
        Assert.Equal(1, requirement.Amount);
        Assert.Equal(3, Choices.TryParseEventChoiceLimit(JToken.Parse(json)));
    }

    private static readonly EncounterChoicePlanParser Choices = new(
        EncounterStructuredParser.NormalizeChoiceEnum
    );

    private static readonly EncounterRewardFilterParser Rewards = new(
        EncounterStructuredParser.NormalizeChoiceEnum
    );

    private static EncounterRewardFilter? ParseRewardFilter(string json) =>
        Rewards.TryParse(JToken.Parse(json));

    private static IReadOnlyList<EncounterStepReference> ParseStepReferences(string json) =>
        Choices
            .TryParseEventChoiceGroups(JToken.Parse(json))
            .SelectMany(group => group.Members)
            .ToList();
}
