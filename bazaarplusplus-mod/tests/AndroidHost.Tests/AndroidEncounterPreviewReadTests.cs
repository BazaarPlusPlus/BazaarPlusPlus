using BazaarPlusPlus.Android.Game;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.EventPreview;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AndroidHost.Tests;

/// <summary>
/// The background read of the game's static-data SQLite file: the level-up plans it keeps beside
/// the encounter plans, keyed by level, with native hero names mapped to their facets.
/// </summary>
public sealed class AndroidEncounterPreviewReadTests : IDisposable
{
    private const string Reward = "1453be13-1d81-45ea-82a5-7d1ef35ec78b";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "bpp-android-static-" + Guid.NewGuid().ToString("N") + ".db"
    );

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    // A step's dealt reward and a package's merchant are read as the desktop reads them, so the
    // tooltip can show the day's odds and name the merchant.
    [Fact]
    public void Reads_each_steps_reward_and_each_packages_merchant()
    {
        var step = Guid.NewGuid();
        var fixedStep = Guid.NewGuid();
        var package = Guid.NewGuid();
        var merchant = Guid.NewGuid();
        using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE cards (Id TEXT NOT NULL PRIMARY KEY, Data BLOB NOT NULL);
                CREATE TABLE level_ups (Id INTEGER NOT NULL PRIMARY KEY, Data BLOB NOT NULL);
                INSERT INTO cards VALUES ('a', CAST($step AS BLOB));
                INSERT INTO cards VALUES ('b', CAST($fixed AS BLOB));
                INSERT INTO cards VALUES ('c', CAST($package AS BLOB));
                """;
            command.Parameters.AddWithValue(
                "$step",
                $$"""
                {
                  "Id": "{{step}}",
                  "Type": "EncounterStep",
                  "Effect": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Groups": [{
                        "$type": "TSpawnGroup",
                        "Filters": [{
                          "$type": "TSpawnFilterQuery",
                          "Constraints": {
                            "$type": "ConstraintAnd",
                            "Constraints": [
                              { "$type": "ConstraintTag", "Tags": ["Tool"], "IsNot": false },
                              { "$type": "ConstraintCardType", "Types": ["Item"], "IsNot": false }
                            ]
                          }
                        }]
                      }],
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 }
                    }
                  }
                }
                """
            );
            command.Parameters.AddWithValue(
                "$fixed",
                $$"""
                {
                  "Id": "{{fixedStep}}",
                  "Type": "EncounterStep",
                  "Effect": {
                    "$type": "TActionGameDealCards",
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Groups": [{
                        "$type": "TSpawnGroup",
                        "Filters": [{ "$type": "TSpawnFilterIdList", "Ids": ["{{Reward}}"] }]
                      }]
                    }
                  }
                }
                """
            );
            command.Parameters.AddWithValue(
                "$package",
                $$"""
                {
                  "Id": "{{package}}",
                  "Type": "Item",
                  "HiddenTags": ["Package"],
                  "Abilities": {
                    "0": {
                      "Prerequisites": [{
                        "$type": "TPrerequisiteRun",
                        "Conditions": {
                          "$type": "TRunConditionalCurrentEncounter",
                          "Conditions": { "$type": "TCardConditionalId", "Id": "{{merchant}}", "IsNot": false }
                        }
                      }]
                    }
                  }
                }
                """
            );
            command.ExecuteNonQuery();
        }

        var plans = AndroidEncounterPreview.Read(_path);

        Assert.True(plans.Snapshot.TryGetTemplate(step, out var stepPlan));
        var reward = Assert.IsType<EncounterRewardFilter>(stepPlan.RewardFilter);
        Assert.Equal(CardType.Item, reward.CardType);
        Assert.Equal(new[] { CardTag.Tool }, reward.Tags);
        Assert.True(reward.UsesDayTierDistribution);
        Assert.True(plans.Snapshot.TryGetTemplate(fixedStep, out var fixedPlan));
        Assert.Null(fixedPlan.RewardFilter);
        Assert.Equal(merchant, plans.PackageMerchants[package]);
        Assert.Single(plans.PackageMerchants);
    }

    [Fact]
    public void Reads_level_ups_beside_the_cards()
    {
        using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE cards (Id TEXT NOT NULL PRIMARY KEY, Data BLOB NOT NULL);
                CREATE TABLE level_ups (Id INTEGER NOT NULL PRIMARY KEY, Data BLOB NOT NULL);
                INSERT INTO cards VALUES ($reward, CAST($card AS BLOB));
                INSERT INTO level_ups VALUES (2, CAST($levelTwo AS BLOB));
                INSERT INTO level_ups VALUES (4, CAST($wrongKey AS BLOB));
                """;
            command.Parameters.AddWithValue("$reward", Reward);
            command.Parameters.AddWithValue(
                "$card",
                $$"""{ "Id": "{{Reward}}", "Type": "Skill", "Heroes": ["Vanessa"] }"""
            );
            command.Parameters.AddWithValue(
                "$levelTwo",
                $$"""
                {
                  "$type": "TLevelUp",
                  "Level": 2,
                  "Rewards": {
                    "$type": "TSpawnContextQuery",
                    "SelectionMethod": "Sequential",
                    "Groups": [{
                      "$type": "TSpawnGroup",
                      "Filters": [{ "$type": "TSpawnFilterIdList", "Ids": ["{{Reward}}"] }],
                      "Limit": { "$type": "TFixedValue", "Value": 1.0 },
                      "Prerequisites": [{
                        "$type": "TPrerequisiteRun",
                        "Conditions": { "$type": "TRunConditionalPlayerHero", "Heroes": ["Dooley", "Jules"], "Operator": "None" }
                      }],
                      "RandomWeight": 0
                    }]
                  },
                  "HealthIncrease": 150
                }
                """
            );
            command.Parameters.AddWithValue("$wrongKey", """{ "Level": 5, "HealthIncrease": 1 }""");
            command.ExecuteNonQuery();
        }

        var plans = AndroidEncounterPreview.Read(_path);

        Assert.Equal(1, plans.Snapshot.LevelUpCount);
        Assert.True(plans.Snapshot.TryGetLevelUp(2, out var plan));
        Assert.Equal(150, plan.HealthIncrease);
        var group = Assert.Single(plan.Groups);
        Assert.Equal(new[] { Guid.Parse(Reward) }, group.TemplateIds);
        var condition = Assert.Single(group.HeroConditions);
        Assert.Equal("None", condition.ComparisonOperator);
        Assert.Equal(new[] { CardHero.Dooley, CardHero.Jules }, condition.Heroes);
        Assert.True(plans.Snapshot.TryGetTemplate(Guid.Parse(Reward), out var reward));
        Assert.Equal(new[] { CardHero.Vanessa }, reward.Heroes);
    }
}
