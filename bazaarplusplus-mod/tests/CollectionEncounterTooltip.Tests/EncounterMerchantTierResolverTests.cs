using BazaarGameShared.Domain.Cards.Encounter.Event;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarGameShared.Domain.Spawning.SpawnBehaviors;
using BazaarGameShared.Domain.Spawning.SpawningContexts;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Localization;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterMerchantTierResolverTests
{
    public EncounterMerchantTierResolverTests()
    {
        L.Install(() => "en", () => BppChineseLocaleMode.Mainland);
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

        Assert.Equal(tier.ToString(), policy.FixedTier.ToString());
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

        Assert.Equal(nameof(ETier.Bronze), policy.FixedTier.ToString());
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

    private static TCardEncounterEvent CreateMerchantTemplate(params ITSpawnBehavior[] behaviors) =>
        new()
        {
            SelectionContext = new TSelectionContext
            {
                SpawnContext = new TSpawnContextQuery { Behaviors = behaviors.ToList() },
            },
        };
}
