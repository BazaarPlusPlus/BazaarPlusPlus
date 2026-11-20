using BazaarPlusPlus.Core.Cards;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EncounterPreview.Tests;

public sealed class EncounterMerchantTierRuleTests
{
    private static readonly EncounterChoicePlanParser Parser = new(
        (kind, raw) => kind == "Tier" && raw == "3" ? "Diamond" : null
    );

    private static EncounterMerchantTierRule Rule(string behaviors) =>
        Parser.TryParseMerchantTierRule(
            JObject.Parse(
                $$"""
                {
                  "Type": "EventEncounter",
                  "Tags": ["Merchant"],
                  "SelectionContext": {
                    "SpawnContext": {
                      "$type": "TSpawnContextQuery",
                      "Behaviors": [{{behaviors}}]
                    }
                  }
                }
                """
            )
        );

    [Fact]
    public void A_numeric_tier_goes_through_the_host_normalization()
    {
        var rule = Rule("""{ "$type": "TSpawnBehaviorTier", "Tiers": [3] }""");

        Assert.Equal(CardTier.Diamond, rule.FixedTier);
    }

    [Fact]
    public void A_pinned_tier_without_a_facet_draws_no_line()
    {
        var rule = Rule("""{ "$type": "TSpawnBehaviorTier", "Tiers": [7] }""");

        Assert.Null(rule.FixedTier);
        Assert.False(rule.UsesDayDistribution);
        Assert.False(rule.IsSupported);
    }

    [Fact]
    public void An_assembly_qualified_type_names_the_behavior()
    {
        var rule = Rule(
            """{ "$type": "BazaarGameShared.Domain.Spawning.SpawnBehaviors.TSpawnBehaviorIgnoreTierTable, BazaarGameShared", "IgnoreTierTable": true }"""
        );

        Assert.Null(rule.FixedTier);
        Assert.False(rule.UsesDayDistribution);
        Assert.False(rule.IsSupported);
    }

    [Fact]
    public void A_merchant_without_a_query_spawn_context_sells_by_the_day()
    {
        Assert.Equal(
            EncounterMerchantTierRule.DayDistribution,
            Parser.TryParseMerchantTierRule(JObject.Parse("""{ "Tags": ["Merchant"] }"""))
        );
        Assert.Equal(
            EncounterMerchantTierRule.DayDistribution,
            Parser.TryParseMerchantTierRule(
                JObject.Parse(
                    """
                    {
                      "SelectionContext": {
                        "SpawnContext": {
                          "$type": "TSpawnContextFixed",
                          "Behaviors": [{ "$type": "TSpawnBehaviorTier", "Tiers": ["Gold"] }]
                        }
                      }
                    }
                    """
                )
            )
        );
    }
}
