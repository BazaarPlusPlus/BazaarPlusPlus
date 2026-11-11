using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Localization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EncounterPreview.Tests;

/// <summary>
/// A merchant's quality rule off the JSON shapes the shared card table
/// (<c>EncounterPreviewCardReaderTests</c>) does not hold: numeric and unmapped tiers, an
/// assembly-qualified <c>$type</c>, and no query spawn context.
/// </summary>
public sealed class EncounterMerchantTierRuleTests
{
    public EncounterMerchantTierRuleTests()
    {
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);
    }

    // The host maps runtime enum numbers to names; raw JSON already carries names.
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
