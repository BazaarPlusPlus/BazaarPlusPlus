using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Localization;
using Xunit;

namespace EventPreview.Tests;

public sealed class GameDataDayTierTableTests
{
    public GameDataDayTierTableTests()
    {
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);
    }

    [Fact]
    public void GameData_table_uses_the_highest_positive_finite_tier_as_its_maximum()
    {
        var table = GameDataDayTierTable.FromWeights(0.89f, 0.1f, 0f, 0.01f);

        Assert.NotNull(table);
        Assert.Equal(CardTier.Diamond, table.MaximumTier);
        Assert.Collection(
            table.Entries,
            entry =>
            {
                Assert.Equal(CardTier.Bronze, entry.Tier);
                Assert.Equal(89d, entry.Percent, 5);
            },
            entry =>
            {
                Assert.Equal(CardTier.Silver, entry.Tier);
                Assert.Equal(10d, entry.Percent, 5);
            },
            entry =>
            {
                Assert.Equal(CardTier.Diamond, entry.Tier);
                Assert.Equal(1d, entry.Percent, 5);
            }
        );
    }

    [Fact]
    public void GameData_table_normalizes_non_unit_weights_and_ignores_unusable_entries()
    {
        var table = GameDataDayTierTable.FromWeights(
            bronze: -1f,
            silver: 2f,
            gold: float.NaN,
            diamond: 6f
        );

        Assert.NotNull(table);
        Assert.Equal(CardTier.Diamond, table.MaximumTier);
        Assert.Collection(
            table.Entries,
            entry => Assert.Equal(25d, entry.Percent, 5),
            entry => Assert.Equal(75d, entry.Percent, 5)
        );
        Assert.Null(GameDataDayTierTable.FromWeights(0f, -1f, float.NaN, float.PositiveInfinity));
    }

    [Theory]
    [InlineData(0.9f, 0.1f, 0f, 0f, "Bronze 90%", null, "Silver 10%")]
    [InlineData(0f, 0.95f, 0.05f, 0f, "Silver 95%", null, "Gold 5%")]
    [InlineData(0f, 0.45f, 0.5f, 0.05f, "Silver 45%", "Gold 50%", "Diamond 5%")]
    public void Quality_line_formats_daily_game_data_weights_without_a_label(
        float bronze,
        float silver,
        float gold,
        float diamond,
        string expectedFirst,
        string? expectedMiddle,
        string expectedLast
    )
    {
        var distribution = GameDataDayTierTable.FromWeights(bronze, silver, gold, diamond);

        var text = EncounterPreviewTextFormatter.BuildQualityLine(distribution, fixedTier: null);

        Assert.DoesNotContain("Quality today", text);
        Assert.Contains(expectedFirst, text);
        if (expectedMiddle != null)
            Assert.Contains(expectedMiddle, text);
        Assert.Contains(expectedLast, text);
    }

    [Fact]
    public void Distribution_normalizes_positive_weights_instead_of_assuming_total_is_one()
    {
        var distribution = GameDataDayTierTable.FromWeights(0f, 0.2f, 0.55f, 0.05f);

        var text = EncounterPreviewTextFormatter.BuildQualityLine(distribution, fixedTier: null);

        Assert.Contains("Silver 25%", text);
        Assert.Contains("Gold 68.75%", text);
        Assert.Contains("Diamond 6.25%", text);
    }

    [Fact]
    public void Distribution_ignores_non_positive_weights_and_rejects_an_empty_table()
    {
        var distribution = GameDataDayTierTable.FromWeights(-1f, 0f, 2f, float.NaN);

        Assert.NotNull(distribution);
        Assert.Single(distribution!.Entries);
        Assert.Equal(CardTier.Gold, distribution.Entries[0].Tier);
        Assert.Equal(100d, distribution.Entries[0].Percent);
        Assert.Null(GameDataDayTierTable.FromWeights(0f, -1f, float.NaN, 0f));
    }

    [Theory]
    [InlineData("zh-CN", false, "青铜 90%", "白银 10%")]
    [InlineData("zh-TW", true, "青銅 90%", "白銀 10%")]
    public void Quality_line_localizes_both_chinese_scripts(
        string language,
        bool traditional,
        string expectedBronze,
        string expectedSilver
    )
    {
        var localeMode = traditional ? BppChineseLocaleMode.Taiwan : BppChineseLocaleMode.Mainland;
        L.Install(() => language, () => localeMode);

        var text = EncounterPreviewTextFormatter.BuildQualityLine(
            GameDataDayTierTable.FromWeights(0.9f, 0.1f, 0f, 0f),
            fixedTier: null
        );

        Assert.Contains(expectedBronze, text);
        Assert.Contains(expectedSilver, text);
        Assert.DoesNotContain("品质：", text);
        Assert.DoesNotContain("品質：", text);
    }

    [Fact]
    public void Quality_line_omits_day_tier_claims_without_a_trustworthy_table()
    {
        var text = EncounterPreviewTextFormatter.BuildQualityLine(dayTiers: null, fixedTier: null);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void Encounter_step_reward_line_shows_quality_for_grab_the_loot_style_rewards()
    {
        var text = EncounterPreviewTextFormatter.BuildRewardQualityLine(
            CreateRewardFilter(usesDayTierTable: true, usesDayTierDistribution: true),
            "Get 3 Loot items",
            GameDataDayTierTable.FromWeights(0.7f, 0.3f, 0f, 0f)
        );

        Assert.Contains("Bronze 70%", text);
        Assert.Contains("Silver 30%", text);
    }

    [Fact]
    public void Encounter_step_reward_line_shows_source_weights_for_downshift_rewards()
    {
        var text = EncounterPreviewTextFormatter.BuildRewardQualityLine(
            CreateRewardFilter(usesDayTierTable: false, usesDayTierDistribution: true),
            "Gain 2 Gold and a Shield item from any Hero",
            GameDataDayTierTable.FromWeights(0.7f, 0.3f, 0f, 0f)
        );

        Assert.Contains("Bronze 70%", text);
        Assert.Contains("Silver 30%", text);
    }

    [Fact]
    public void Encounter_step_reward_line_skips_fixed_or_inherited_quality()
    {
        var text = EncounterPreviewTextFormatter.BuildRewardQualityLine(
            CreateRewardFilter(usesDayTierTable: false, usesDayTierDistribution: false),
            "Get an item",
            GameDataDayTierTable.FromWeights(0.7f, 0.3f, 0f, 0f)
        );

        Assert.Equal(string.Empty, text);
    }

    private static EncounterRewardFilter CreateRewardFilter(
        bool usesDayTierTable,
        bool usesDayTierDistribution
    ) =>
        new(
            CardType.Item,
            quantity: 1,
            fromAnyHero: false,
            Array.Empty<CardSize>(),
            new[] { CardTier.Bronze, CardTier.Silver, CardTier.Gold, CardTier.Diamond },
            Array.Empty<CardTag>(),
            Array.Empty<CardHiddenTag>(),
            "Item",
            usesDayTierTable: usesDayTierTable,
            usesDayTierDistribution: usesDayTierDistribution
        );
}
