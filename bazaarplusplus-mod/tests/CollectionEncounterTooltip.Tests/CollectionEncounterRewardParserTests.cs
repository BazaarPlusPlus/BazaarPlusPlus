using BazaarGameShared.Domain.Core.Types;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class EncounterRewardParserTests
{
    [Fact]
    public void TryParse_extracts_item_pool_from_event_result_text()
    {
        var reward = EncounterRewardParser.TryParse(
            "(if you are Dooley or Stelle) Get a Small Silver-tier Tool from any Hero"
        );

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Item, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.True(reward.FromAnyHero);
        Assert.Equal(new[] { ECardSize.Small }, reward.Sizes);
        Assert.Equal(new[] { ETier.Silver }, reward.Tiers);
        Assert.Equal(new[] { ECardTag.Tool }, reward.Tags);
        Assert.Empty(reward.Keywords);
        Assert.Equal("Small Silver Tool", reward.FilterSummary);
    }

    [Fact]
    public void TryParse_extracts_skill_pool_from_event_result_text()
    {
        var reward = EncounterRewardParser.TryParse("Learn 1 Freeze skill");

        Assert.NotNull(reward);
        Assert.Equal(ECardType.Skill, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.Empty(reward.Sizes);
        Assert.Empty(reward.Tiers);
        Assert.Empty(reward.Tags);
        Assert.Equal(new[] { EHiddenTag.Freeze }, reward.Keywords);
        Assert.Equal("Freeze skill", reward.FilterSummary);
    }

    [Fact]
    public void TryParse_ignores_non_card_player_stat_results()
    {
        var reward = EncounterRewardParser.TryParse("Permanently gain 3 Regen");

        Assert.Null(reward);
    }
}
