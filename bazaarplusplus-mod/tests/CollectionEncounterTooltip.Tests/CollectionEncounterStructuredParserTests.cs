using BazaarGameShared.Domain.Core.Types;
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

        var references = EncounterStructuredParser.TryParseEventStepReferences(json);

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

        var reference = Assert.Single(EncounterStructuredParser.TryParseEventStepReferences(json));

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

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(new[] { ECardSize.Medium }, reward.Sizes);
        Assert.Equal(new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond }, reward.Tiers);
        Assert.Empty(reward.Tags);
        Assert.Equal(new[] { ECardTag.Loot }, reward.ExcludedTags);
        Assert.Equal("Medium Bronze-Diamond Item, not Loot", reward.FilterSummary);
    }

    [Fact]
    public void TryParseRewardFilter_reads_runtime_deal_card_action_without_json_type_metadata()
    {
        var step = new RuntimeEncounterStep
        {
            Abilities = new Dictionary<string, RuntimeAbility>
            {
                ["0"] = new()
                {
                    Action = new TActionGameDealCards
                    {
                        SpawnContext = new RuntimeSpawnContext
                        {
                            Limit = new RuntimeFixedValue { Value = 1.0 },
                            Groups = new[]
                            {
                                new RuntimeSpawnGroup
                                {
                                    Filters = new[]
                                    {
                                        new RuntimeSpawnFilter
                                        {
                                            Constraints = new object[]
                                            {
                                                new ConstraintCardType
                                                {
                                                    Types = new[] { "Skill" },
                                                },
                                                new ConstraintHiddenTag
                                                {
                                                    Tags = new[] { "Freeze" },
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var reward = EncounterStructuredParser.TryParseRewardFilter(step);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Skill, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(new[] { EHiddenTag.Freeze }, reward.Keywords);
    }

    [Fact]
    public void TryParseRewardFilter_reads_runtime_constraint_and_children()
    {
        var step = new RuntimeEncounterStep
        {
            Abilities = new Dictionary<string, RuntimeAbility>
            {
                ["0"] = new()
                {
                    Action = new TActionGameDealCards
                    {
                        SpawnContext = new RuntimeSpawnContext
                        {
                            Limit = new RuntimeFixedValue { Value = 1.0 },
                            Groups = new[]
                            {
                                new RuntimeSpawnGroup
                                {
                                    Filters = new[]
                                    {
                                        new RuntimeSpawnFilter
                                        {
                                            Constraints = new object[]
                                            {
                                                new ConstraintAnd
                                                {
                                                    Constraints = new object[]
                                                    {
                                                        new ConstraintCardType
                                                        {
                                                            Types = new[] { "Item" },
                                                        },
                                                        new ConstraintSize
                                                        {
                                                            Sizes = new[] { "Medium" },
                                                        },
                                                        new ConstraintTier
                                                        {
                                                            Tiers = new[]
                                                            {
                                                                "Bronze",
                                                                "Silver",
                                                                "Gold",
                                                                "Diamond",
                                                            },
                                                        },
                                                        new ConstraintTag
                                                        {
                                                            Tags = new[] { "Loot" },
                                                            IsNot = true,
                                                        },
                                                    },
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var reward = EncounterStructuredParser.TryParseRewardFilter(step);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(new[] { ECardSize.Medium }, reward.Sizes);
        Assert.Equal(new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond }, reward.Tiers);
        Assert.Equal(new[] { ECardTag.Loot }, reward.ExcludedTags);
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

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Equal(new[] { ECardSize.Small, ECardSize.Medium }, reward.Sizes);
        Assert.Equal(new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond }, reward.Tiers);
        Assert.Equal(new[] { ECardTag.Tool }, reward.Tags);
        Assert.Equal("Small Medium Bronze-Diamond Tool", reward.FilterSummary);
    }

    [Fact]
    public void TryParseRewardFilter_applies_spawn_behavior_tier_as_an_exact_reward_tier()
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
                      "Limit": { "$type": "TFixedValue", "Value": 2.0 },
                      "Groups": [
                        {
                          "Filters": [
                            {
                              "Constraints": {
                                "$type": "ConstraintAnd",
                                "Constraints": [
                                  { "$type": "ConstraintCardType", "Types": ["Item"], "IsNot": false },
                                  { "$type": "ConstraintTag", "Tags": ["Food"], "IsNot": false },
                                  { "$type": "ConstraintTier", "Tiers": ["Legendary"], "IsNot": true }
                                ]
                              }
                            }
                          ]
                        }
                      ],
                      "Behaviors": [
                        { "$type": "TSpawnBehaviorTier", "Tiers": ["Diamond"], "IsNot": false },
                        { "$type": "TSpawnBehaviorIgnoreTierTable", "IgnoreTierTable": true }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Equal(2, reward.Quantity);
        Assert.Equal(new[] { ETier.Diamond }, reward.Tiers);
        Assert.Equal(new[] { ECardTag.Food }, reward.Tags);
        Assert.Equal("Diamond Food", reward.FilterSummary);
        Assert.False(reward.UsesDayTierTable);
        Assert.False(reward.UsesDayTierDistribution);
    }

    // An exclusion-only tier behavior ("any tier except X") leaves the pool governed by
    // the day tier table; only an inclusive tier list pins the pool to fixed tiers.
    [Fact]
    public void TryParseRewardFilter_keeps_day_tier_table_for_exclusion_only_spawn_behavior_tier()
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
                                { "$type": "ConstraintCardType", "Types": ["Item"] }
                              ]
                            }
                          ]
                        }
                      ],
                      "Behaviors": [
                        { "$type": "TSpawnBehaviorTier", "Tiers": ["Bronze"], "IsNot": true }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(
            new[] { ETier.Silver, ETier.Gold, ETier.Diamond, ETier.Legendary },
            reward.Tiers
        );
        Assert.True(reward.UsesDayTierTable);
        Assert.True(reward.UsesDayTierDistribution);
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

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(new[] { ETier.Diamond }, reward.Tiers);
    }

    [Fact]
    public void TryParseRewardFilter_reads_runtime_spawn_behavior_tier()
    {
        var step = new RuntimeEncounterStep
        {
            Abilities = new Dictionary<string, RuntimeAbility>
            {
                ["0"] = new()
                {
                    Action = new TActionGameDealCards
                    {
                        SpawnContext = new RuntimeSpawnContext
                        {
                            Limit = new RuntimeFixedValue { Value = 2.0 },
                            Groups = new[]
                            {
                                new RuntimeSpawnGroup
                                {
                                    Filters = new[]
                                    {
                                        new RuntimeSpawnFilter
                                        {
                                            Constraints = new object[]
                                            {
                                                new ConstraintCardType { Types = new[] { "Item" } },
                                                new ConstraintTag { Tags = new[] { "Food" } },
                                                new ConstraintTier
                                                {
                                                    Tiers = new[] { "Legendary" },
                                                    IsNot = true,
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            Behaviors = new object[]
                            {
                                new TSpawnBehaviorTier { Tiers = new[] { "Diamond" } },
                                new TSpawnBehaviorIgnoreTierTable(),
                            },
                        },
                    },
                },
            },
        };

        var reward = EncounterStructuredParser.TryParseRewardFilter(step);

        Assert.NotNull(reward);
        Assert.Equal(2, reward.Quantity);
        Assert.Equal(new[] { ETier.Diamond }, reward.Tiers);
        Assert.Equal(new[] { ECardTag.Food }, reward.Tags);
        Assert.False(reward.UsesDayTierTable);
        Assert.False(reward.UsesDayTierDistribution);
    }

    [Fact]
    public void TryParseRewardFilter_keeps_day_tier_table_for_exclusion_only_runtime_behavior()
    {
        var step = new RuntimeEncounterStep
        {
            Abilities = new Dictionary<string, RuntimeAbility>
            {
                ["0"] = new()
                {
                    Action = new TActionGameDealCards
                    {
                        SpawnContext = new RuntimeSpawnContext
                        {
                            Limit = new RuntimeFixedValue { Value = 1.0 },
                            Groups = new[]
                            {
                                new RuntimeSpawnGroup
                                {
                                    Filters = new[]
                                    {
                                        new RuntimeSpawnFilter
                                        {
                                            Constraints = new object[]
                                            {
                                                new ConstraintCardType { Types = new[] { "Item" } },
                                            },
                                        },
                                    },
                                },
                            },
                            Behaviors = new object[]
                            {
                                new TSpawnBehaviorTier { Tiers = new[] { "Bronze" }, IsNot = true },
                            },
                        },
                    },
                },
            },
        };

        var reward = EncounterStructuredParser.TryParseRewardFilter(step);

        Assert.NotNull(reward);
        Assert.Equal(
            new[] { ETier.Silver, ETier.Gold, ETier.Diamond, ETier.Legendary },
            reward.Tiers
        );
        Assert.True(reward.UsesDayTierTable);
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

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(1, reward.Quantity);
        Assert.Equal(new[] { ECardTag.Weapon }, reward.Tags);
        Assert.False(reward.UsesDayTierTable);
        Assert.False(reward.UsesDayTierDistribution);
    }

    [Fact]
    public void TryParseRewardFilter_keeps_source_day_distribution_for_downshift_rewards()
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
                                { "$type": "ConstraintHiddenTag", "HiddenTags": ["Shield"] }
                              ]
                            }
                          ]
                        }
                      ],
                      "Behaviors": [
                        { "$type": "TSpawnBehaviorDownShiftTier" },
                        { "$type": "TSpawnBehaviorInheritTier", "Inherits": false }
                      ]
                    }
                  }
                }
              }
            }
            """;

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.False(reward.UsesDayTierTable);
        Assert.True(reward.UsesDayTierDistribution);
    }

    [Fact]
    public void TryParseRewardFilter_marks_runtime_inherit_tier_rewards_as_not_day_tier_driven()
    {
        var step = new RuntimeEncounterStep
        {
            Abilities = new Dictionary<string, RuntimeAbility>
            {
                ["0"] = new()
                {
                    Action = new TActionGameDealCards
                    {
                        SpawnContext = new RuntimeSpawnContext
                        {
                            Limit = new RuntimeFixedValue { Value = 1.0 },
                            Groups = new[]
                            {
                                new RuntimeSpawnGroup
                                {
                                    Filters = new[]
                                    {
                                        new RuntimeSpawnFilter
                                        {
                                            Constraints = new object[]
                                            {
                                                new ConstraintCardType
                                                {
                                                    Types = new[] { "Skill" },
                                                },
                                                new ConstraintHiddenTag
                                                {
                                                    Tags = new[] { "Weapon" },
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            Behaviors = new object[]
                            {
                                new TSpawnBehaviorInheritTier { Inherits = true },
                            },
                        },
                    },
                },
            },
        };

        var reward = EncounterStructuredParser.TryParseRewardFilter(step);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Skill, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.False(reward.UsesDayTierTable);
        Assert.False(reward.UsesDayTierDistribution);
    }

    [Fact]
    public void TryParseRewardFilter_reads_real_constraint_is_not_as_exclusion()
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

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Empty(reward.Tags);
        Assert.Equal(new[] { ECardTag.Loot }, reward.ExcludedTags);
        Assert.Equal("Medium Bronze-Diamond Item, not Loot", reward.FilterSummary);
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

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Equal(new[] { ECardSize.Small }, reward.Sizes);
        Assert.Equal(new[] { ETier.Silver }, reward.Tiers);
        Assert.Equal(new[] { ECardTag.Food }, reward.Tags);
    }

    [Fact]
    public void TryParseRewardFilter_merges_alternative_filters_of_the_same_card_type()
    {
        // Weighted alternatives (e.g. a 95%/5% split) of the same card type describe one
        // pool for display purposes and are unioned.
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

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward!.CardType);
        Assert.Equal(new[] { ECardSize.Small, ECardSize.Large }, reward.Sizes);
        Assert.Equal(new[] { ECardTag.Tool, ECardTag.Weapon }, reward.Tags);
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

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

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

        var reward = EncounterStructuredParser.TryParseRewardFilter(json);

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Skill, reward.CardType);
        Assert.Equal(new[] { EHiddenTag.Freeze }, reward.Keywords);
    }

    private sealed class RuntimeEncounterStep
    {
        public Dictionary<string, RuntimeAbility> Abilities { get; init; } = new();
    }

    private sealed class RuntimeAbility
    {
        public object? Action { get; init; }
    }

    private sealed class TActionGameDealCards
    {
        public RuntimeSpawnContext? SpawnContext { get; init; }
    }

    private sealed class RuntimeSpawnContext
    {
        public RuntimeFixedValue? Limit { get; init; }

        public RuntimeSpawnGroup[] Groups { get; init; } = Array.Empty<RuntimeSpawnGroup>();

        public object[] Behaviors { get; init; } = Array.Empty<object>();
    }

    private sealed class RuntimeFixedValue
    {
        public double Value { get; init; }
    }

    private sealed class RuntimeSpawnGroup
    {
        public RuntimeSpawnFilter[] Filters { get; init; } = Array.Empty<RuntimeSpawnFilter>();
    }

    private sealed class RuntimeSpawnFilter
    {
        public object[] Constraints { get; init; } = Array.Empty<object>();
    }

    private sealed class ConstraintCardType
    {
        public string[] Types { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class ConstraintHiddenTag
    {
        public string[] Tags { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class ConstraintAnd
    {
        public object[] Constraints { get; init; } = Array.Empty<object>();
    }

    private sealed class ConstraintSize
    {
        public string[] Sizes { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class ConstraintTier
    {
        public string[] Tiers { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class ConstraintTag
    {
        public string[] Tags { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class TSpawnBehaviorTier
    {
        public string[] Tiers { get; init; } = Array.Empty<string>();

        public bool IsNot { get; init; }
    }

    private sealed class TSpawnBehaviorIgnoreTierTable
    {
        public bool IgnoreTierTable { get; init; } = true;
    }

    private sealed class TSpawnBehaviorInheritTier
    {
        public bool Inherits { get; init; } = true;
    }

    [Fact]
    public void TryParseEventChoiceGroups_marks_random_selection_groups_as_pools()
    {
        // Advanced Training shape: outer Sequential limit 3, one group that itself
        // selects randomly over its member ids.
        var json = """
            {
              "$type": "TCardEncounterEvent",
              "SelectionContext": {
                "SpawnContext": {
                  "SelectionMethod": "Sequential",
                  "Limit": { "$type": "TFixedValue", "Value": 3.0 },
                  "Groups": [
                    {
                      "SelectionMethod": "Random",
                      "RandomWeight": 2,
                      "Filters": [
                        {
                          "$type": "TSpawnFilterIdList",
                          "Ids": [
                            "10000000-0000-0000-0000-000000000001",
                            "10000000-0000-0000-0000-000000000002"
                          ]
                        }
                      ]
                    },
                    {
                      "SelectionMethod": "Sequential",
                      "Filters": [
                        { "$type": "TSpawnFilterIdList", "Ids": ["10000000-0000-0000-0000-000000000003"] }
                      ]
                    }
                  ]
                }
              }
            }
            """;

        var groups = EncounterStructuredParser.TryParseEventChoiceGroups(json);

        Assert.Equal(2, groups.Count);
        Assert.True(groups[0].IsRandomPool);
        Assert.Equal(2, groups[0].Members.Count);
        Assert.False(groups[1].IsRandomPool);
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

        var references = EncounterStructuredParser.TryParseEventStepReferences(json);

        var reference = Assert.Single(references);
        var requirement = Assert.Single(reference.Requirements);
        Assert.Equal(2, requirement.TagCandidateGroups.Count);
        Assert.Equal(new[] { "Toy" }, requirement.TagCandidateGroups[0]);
        Assert.Equal(new[] { "Drone" }, requirement.TagCandidateGroups[1]);
        Assert.Equal("Any", requirement.TagOperator);
        Assert.Equal("GreaterThanOrEqual", requirement.Comparison);
        Assert.Equal(1, requirement.Amount);
        Assert.Equal(3, EncounterStructuredParser.TryParseEventChoiceLimit(json));
    }
}
