using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Localization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EventPreview.Tests;

/// <summary>
/// A merchant's quality rule read off its card JSON, the Android host's input, held to the
/// desktop's typed <c>EncounterMerchantTierResolver</c> cases.
/// </summary>
public sealed class EncounterMerchantTierRuleTests
{
    public EncounterMerchantTierRuleTests()
    {
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);
    }

    // The host maps runtime enum numbers to names; raw JSON already carries names.
    private static readonly EncounterChoicePlanParser Parser = new(
        (kind, raw) => kind == "Tier" && raw == "3" ? "Diamond" : null,
        (_, _, _, _) => null
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

    [Theory]
    [InlineData("Bronze", CardTier.Bronze)]
    [InlineData("Gold", CardTier.Gold)]
    [InlineData("Diamond", CardTier.Diamond)]
    public void Fixed_tier_merchants_do_not_use_the_daily_distribution(string name, CardTier tier)
    {
        var rule = Rule(
            $$"""
            { "$type": "TSpawnBehaviorTier", "Tiers": ["{{name}}"], "IsNot": false },
            { "$type": "TSpawnBehaviorIgnoreTierTable", "IgnoreTierTable": true }
            """
        );

        Assert.Equal(tier, rule.FixedTier);
        Assert.False(rule.UsesDayDistribution);
        Assert.True(rule.IsSupported);
    }

    [Fact]
    public void Curio_style_fixed_tier_does_not_require_ignore_tier_table_behavior()
    {
        var rule = Rule("""{ "$type": "TSpawnBehaviorTier", "Tiers": ["Gold"] }""");

        Assert.Equal(CardTier.Gold, rule.FixedTier);
        Assert.False(rule.UsesDayDistribution);
    }

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
    public void Exclusions_and_several_tiers_keep_the_daily_distribution()
    {
        Assert.Equal(
            EncounterMerchantTierRule.DayDistribution,
            Rule("""{ "$type": "TSpawnBehaviorTier", "Tiers": ["Bronze"], "IsNot": true }""")
        );
        Assert.Equal(
            EncounterMerchantTierRule.DayDistribution,
            Rule("""{ "$type": "TSpawnBehaviorTier", "Tiers": ["Gold", "Diamond"] }""")
        );
    }

    [Fact]
    public void Downshift_merchants_still_show_the_raw_daily_weights()
    {
        var rule = Rule("""{ "$type": "TSpawnBehaviorDownShiftTier" }""");

        Assert.Equal(EncounterMerchantTierRule.DayDistribution, rule);
    }

    [Fact]
    public void Ignore_tier_table_without_a_fixed_tier_suppresses_the_daily_line()
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

    [Fact]
    public void A_fixed_tier_merchant_renders_one_quality_line()
    {
        var line = EncounterPreviewTextFormatter.BuildQualityLine(null, CardTier.Diamond);

        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Equal(
            string.Empty,
            EncounterPreviewTextFormatter.BuildQualityLine(null, fixedTier: null)
        );
    }
}
