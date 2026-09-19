using BazaarGameShared.Domain.Cards.Encounter.Event;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Domain.Spawning.SpawnBehaviors;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarPlusPlus.GameInterop.DayTiers;
using BazaarPlusPlus.Localization;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class GameDataDayTierTableTests
{
    public GameDataDayTierTableTests()
    {
        L.Install(new TestLanguageProvider(), new TestLocaleModeProvider());
    }

    [Fact]
    public void GameData_table_uses_the_highest_positive_finite_tier_as_its_maximum()
    {
        var table = GameDataDayTierTable.FromWeights(0.89f, 0.1f, 0f, 0.01f);

        Assert.NotNull(table);
        Assert.Equal(ETier.Diamond, table.MaximumTier);
        Assert.Collection(
            table.Entries,
            entry =>
            {
                Assert.Equal(ETier.Bronze, entry.Tier);
                Assert.Equal(89d, entry.Percent, 5);
            },
            entry =>
            {
                Assert.Equal(ETier.Silver, entry.Tier);
                Assert.Equal(10d, entry.Percent, 5);
            },
            entry =>
            {
                Assert.Equal(ETier.Diamond, entry.Tier);
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
        Assert.Equal(ETier.Diamond, table.MaximumTier);
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
        Assert.Equal(ETier.Gold, distribution.Entries[0].Tier);
        Assert.Equal(100d, distribution.Entries[0].Percent);
        Assert.Null(GameDataDayTierTable.FromWeights(0f, -1f, float.NaN, 0f));
    }

    [Theory]
    [InlineData(ETier.Bronze)] // Curio
    [InlineData(ETier.Silver)] // Silvia
    [InlineData(ETier.Gold)] // Goldie
    [InlineData(ETier.Diamond)] // Luxe
    public void Fixed_tier_merchants_do_not_use_the_daily_distribution(ETier tier)
    {
        var template = CreateMerchantTemplate(
            new TSpawnBehaviorTier
            {
                Tiers = new HashSet<ETier> { tier },
                IsNot = false,
            },
            new TSpawnBehaviorIgnoreTierTable { IgnoreTierTable = true }
        );

        var policy = EncounterMerchantTierResolver.Resolve(template);
        var text = EncounterPreviewTextFormatter.BuildQualityLine(
            GameDataDayTierTable.FromWeights(0.9f, 0.1f, 0f, 0f),
            policy.FixedTier
        );

        Assert.Equal(tier, policy.FixedTier);
        Assert.False(policy.UsesDayDistribution);
        Assert.Contains($">{tier}</color>", text);
        Assert.DoesNotContain("Item quality", text);
        Assert.DoesNotContain("90%", text);
    }

    [Fact]
    public void Curio_style_fixed_tier_does_not_require_ignore_tier_table_behavior()
    {
        var template = CreateMerchantTemplate(
            new TSpawnBehaviorTier
            {
                Tiers = new HashSet<ETier> { ETier.Bronze },
                IsNot = false,
            }
        );

        var policy = EncounterMerchantTierResolver.Resolve(template);

        Assert.Equal(ETier.Bronze, policy.FixedTier);
        Assert.False(policy.UsesDayDistribution);
    }

    [Fact]
    public void Downshift_merchants_still_show_the_raw_daily_weights()
    {
        var policy = EncounterMerchantTierResolver.Resolve(
            CreateMerchantTemplate(new TSpawnBehaviorDownShiftTier())
        );

        Assert.Null(policy.FixedTier);
        Assert.True(policy.UsesDayDistribution);
    }

    [Fact]
    public void Ignore_tier_table_without_a_fixed_tier_suppresses_the_daily_line()
    {
        var policy = EncounterMerchantTierResolver.Resolve(
            CreateMerchantTemplate(new TSpawnBehaviorIgnoreTierTable { IgnoreTierTable = true })
        );

        Assert.Null(policy.FixedTier);
        Assert.False(policy.UsesDayDistribution);
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
        L.Install(new TestLanguageProvider(language), new TestLocaleModeProvider(localeMode));

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
            ECardType.Item,
            quantity: 1,
            fromAnyHero: false,
            Array.Empty<ECardSize>(),
            new[] { ETier.Bronze, ETier.Silver, ETier.Gold, ETier.Diamond },
            Array.Empty<ECardTag>(),
            Array.Empty<EHiddenTag>(),
            "Item",
            usesDayTierTable: usesDayTierTable,
            usesDayTierDistribution: usesDayTierDistribution
        );

    private static TCardEncounterEvent CreateMerchantTemplate(params ITSpawnBehavior[] behaviors) =>
        new()
        {
            SelectionContext = new TSelectionContext
            {
                SpawnContext = new TSpawnContextQuery { Behaviors = behaviors.ToList() },
            },
        };

    private sealed class TestLanguageProvider : ILanguageProvider
    {
        public TestLanguageProvider(string languageCode = "en")
        {
            CurrentLanguageCode = languageCode;
        }

        public string CurrentLanguageCode { get; }
    }

    private sealed class TestLocaleModeProvider : ILocaleModeProvider
    {
        public TestLocaleModeProvider(BppChineseLocaleMode mode = BppChineseLocaleMode.Mainland)
        {
            CurrentMode = mode;
        }

        public BppChineseLocaleMode CurrentMode { get; }
    }
}
