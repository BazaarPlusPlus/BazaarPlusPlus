using BazaarPlusPlus.Android.Game;
using BazaarPlusPlus.Core.Cards;
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
