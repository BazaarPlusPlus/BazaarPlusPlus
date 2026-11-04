#nullable enable
using BazaarPlusPlus.Core.Cards;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EventPreview.Tests;

/// <summary>
/// A dealt reward read off a card's static-data JSON, the shapes GameData stores: what makes a
/// filter, and the day-table rules the quality odds follow.
/// </summary>
public sealed class EncounterRewardFilterParserTests
{
    private const string ToolItemFilter = """
        {
          "$type": "TSpawnFilterQuery",
          "Constraints": {
            "$type": "ConstraintAnd",
            "Constraints": [
              { "$type": "ConstraintTag", "Tags": ["Tool"], "IsNot": false },
              { "$type": "ConstraintCardType", "Types": ["Item"], "IsNot": false }
            ]
          }
        }
        """;

    // Raw JSON carries enum names, so the host's normalization has nothing to add.
    private static readonly EncounterRewardFilterParser Parser = new((_, _) => null);

    private static JObject Card(string groups, string behaviors = "null") =>
        JObject.Parse(
            $$"""
            {
              "Type": "EncounterStep",
              "Abilities": {
                "0": {
                  "Action": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Groups": {{groups}},
                      "SelectionMethod": "Sequential",
                      "Limit": { "$type": "TFixedValue", "Value": 2.0 },
                      "Behaviors": {{behaviors}}
                    }
                  }
                }
              }
            }
            """
        );

    private static string Group(string filter, string prerequisites = "null") =>
        $$"""
            [{
              "$type": "TSpawnGroup",
              "Filters": [{{filter}}],
              "Prerequisites": {{prerequisites}},
              "Behaviors": null
            }]
            """;

    [Fact]
    public void A_query_filter_reads_type_tags_and_quantity_and_follows_the_day_table()
    {
        var reward = Parser.TryParse(Card(Group(ToolItemFilter)));

        Assert.NotNull(reward);
        Assert.Equal(CardType.Item, reward.CardType);
        Assert.Equal(new[] { CardTag.Tool }, reward.Tags);
        Assert.Equal(2, reward.Quantity);
        Assert.True(reward.UsesDayTierTable);
        Assert.True(reward.UsesDayTierDistribution);
    }

    // "Work as Maintenance": a down-shifted reward starts from the day's table, and a disabled
    // inherit-tier behavior changes nothing.
    [Fact]
    public void A_down_shift_keeps_the_source_distribution()
    {
        var reward = Parser.TryParse(
            Card(
                Group(ToolItemFilter),
                """
                [
                  { "$type": "TSpawnBehaviorDownShiftTier" },
                  { "$type": "TSpawnBehaviorIgnoreHero", "IgnoreHero": true },
                  { "$type": "TSpawnBehaviorInheritTier", "Inherits": false }
                ]
                """
            )
        );

        Assert.NotNull(reward);
        Assert.False(reward.UsesDayTierTable);
        Assert.True(reward.UsesDayTierDistribution);
    }

    [Theory]
    [InlineData("""{ "$type": "TSpawnBehaviorIgnoreTierTable", "IgnoreTierTable": true }""")]
    [InlineData("""{ "$type": "TSpawnBehaviorInheritTier", "Inherits": true }""")]
    [InlineData("""{ "$type": "TSpawnBehaviorTier", "Tiers": ["Gold"], "IsNot": false }""")]
    public void A_behavior_that_replaces_the_tier_leaves_no_distribution(string behavior)
    {
        var reward = Parser.TryParse(Card(Group(ToolItemFilter), $"[{behavior}]"));

        Assert.NotNull(reward);
        Assert.False(reward.UsesDayTierDistribution);
    }

    // A list of fixed ids is no filter, however its group is gated: the desktop reads no filter
    // from it, and a prerequisite's tag must not read as the reward's.
    [Fact]
    public void A_fixed_id_list_is_no_filter_even_beside_a_tagged_prerequisite()
    {
        var reward = Parser.TryParse(
            Card(
                Group(
                    """{ "$type": "TSpawnFilterIdList", "Ids": ["74d5ddf6-3654-45e7-8859-483bd8c84ff7"] }""",
                    """
                    [{
                      "$type": "TPrerequisiteCardCount",
                      "Subject": {
                        "$type": "TTargetCardSection",
                        "Conditions": { "$type": "ConstraintTag", "Tags": ["Tool"], "IsNot": false }
                      },
                      "Amount": 1
                    }]
                    """
                )
            )
        );

        Assert.Null(reward);
    }

    // The desktop's typed route skips a ConstraintOr; its branches are alternatives, not a
    // conjunction of constraints.
    [Fact]
    public void The_branches_of_an_or_constrain_nothing()
    {
        var reward = Parser.TryParse(
            Card(
                Group(
                    """
                    {
                      "$type": "TSpawnFilterQuery",
                      "Constraints": {
                        "$type": "ConstraintAnd",
                        "Constraints": [
                          { "$type": "ConstraintCardType", "Types": ["Item"], "IsNot": false },
                          {
                            "$type": "ConstraintOr",
                            "Constraints": [
                              { "$type": "ConstraintTag", "Tags": ["Tool"], "IsNot": false },
                              { "$type": "ConstraintTag", "Tags": ["Weapon"], "IsNot": false }
                            ]
                          }
                        ]
                      }
                    }
                    """
                )
            )
        );

        Assert.NotNull(reward);
        Assert.Empty(reward.Tags);
    }

    // A runtime object serializes enums as the game's integers. BPP's facet integers mean
    // nothing (ADR-0015), so an integer reads only through the host's names.
    [Fact]
    public void An_integer_reads_through_the_hosts_names_and_never_as_a_facet()
    {
        const string filter = """
            {
              "$type": "TSpawnFilterQuery",
              "Constraints": {
                "$type": "ConstraintAnd",
                "Constraints": [
                  { "$type": "ConstraintTier", "Tiers": [2], "IsNot": false },
                  { "$type": "ConstraintCardType", "Types": ["Item"], "IsNot": false }
                ]
              }
            }
            """;

        var named = new EncounterRewardFilterParser(
            (kind, raw) => kind == "Tier" && raw == "2" ? "Diamond" : null
        ).TryParse(Card(Group(filter)));
        var unnamed = Parser.TryParse(Card(Group(filter)));

        Assert.Equal(new[] { CardTier.Diamond }, named!.Tiers);
        Assert.Empty(unnamed!.Tiers);
    }

    [Fact]
    public void A_card_without_a_deal_action_has_no_reward()
    {
        Assert.Null(Parser.TryParse(JObject.Parse("""{ "Type": "EncounterStep" }""")));
        Assert.Null(Parser.TryParse(null));
    }

    [Fact]
    public void A_random_pool_reads_the_filter_with_the_contexts_and_groups_behaviors()
    {
        var pool = Parser.ParsePool(
            JObject.Parse(
                """{ "Behaviors": [{ "$type": "TSpawnBehaviorIgnoreTierTable", "IgnoreTierTable": true }] }"""
            ),
            JObject.Parse("""{ "Behaviors": null }"""),
            JObject.Parse(ToolItemFilter),
            3
        );

        Assert.NotNull(pool);
        Assert.Equal(CardType.Item, pool.CardType);
        Assert.Equal(3, pool.Quantity);
        Assert.False(pool.UsesDayTierDistribution);
    }

    private static readonly EncounterChoicePlanParser Choices = new((_, _) => null);

    private static JObject Package(string abilities) =>
        JObject.Parse(
            $$"""{ "Type": "Item", "HiddenTags": ["Package"], "Abilities": {{abilities}} }"""
        );

    private static string Gate(Guid merchant, bool isNot = false) =>
        $$"""
            {
              "Prerequisites": [{
                "$type": "TPrerequisiteRun",
                "Conditions": {
                  "$type": "TRunConditionalCurrentEncounter",
                  "Conditions": { "$type": "TCardConditionalId", "Id": "{{merchant}}", "IsNot": {{isNot.ToString().ToLowerInvariant()}} }
                }
              }]
            }
            """;

    [Fact]
    public void A_package_names_the_encounter_its_abilities_gate_on()
    {
        var merchant = Guid.NewGuid();

        Assert.Equal(
            merchant,
            Choices.TryParsePackageMerchant(
                Package($$"""{ "0": {{Gate(merchant)}}, "1": {{Gate(merchant)}} }""")
            )
        );
    }

    [Fact]
    public void A_package_with_two_encounters_or_an_excluding_gate_names_none()
    {
        Assert.Null(
            Choices.TryParsePackageMerchant(
                Package($$"""{ "0": {{Gate(Guid.NewGuid())}}, "1": {{Gate(Guid.NewGuid())}} }""")
            )
        );
        Assert.Null(
            Choices.TryParsePackageMerchant(
                Package($$"""{ "0": {{Gate(Guid.NewGuid(), isNot: true)}} }""")
            )
        );
    }

    [Fact]
    public void Only_a_package_names_a_merchant()
    {
        var card = JObject.Parse(
            $$"""{ "Type": "Item", "HiddenTags": [], "Abilities": { "0": {{Gate(Guid.NewGuid())}} } }"""
        );

        Assert.Null(Choices.TryParsePackageMerchant(card));
    }
}
