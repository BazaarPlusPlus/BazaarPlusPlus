using BazaarPlusPlus.Core.Cards;
using Xunit;

namespace EventPreview.Tests;

public sealed class EncounterRewardParserTests
{
    [Fact]
    public void TryParse_extracts_item_pool_from_event_result_text()
    {
        var reward = EncounterRewardParser.TryParse(
            "(if you are Dooley or Stelle) Get a Small Silver-tier Tool from any Hero"
        );

        Assert.NotNull(reward);
        Assert.Equal(CardType.Item, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.True(reward.FromAnyHero);
        Assert.Equal(new[] { CardSize.Small }, reward.Sizes);
        Assert.Equal(new[] { CardTier.Silver }, reward.Tiers);
        Assert.Equal(new[] { CardTag.Tool }, reward.Tags);
        Assert.Empty(reward.Keywords);
        Assert.Equal("Small Silver Tool", reward.FilterSummary);
    }

    [Fact]
    public void TryParse_extracts_skill_pool_from_event_result_text()
    {
        var reward = EncounterRewardParser.TryParse("Learn 1 Freeze skill");

        Assert.NotNull(reward);
        Assert.Equal(CardType.Skill, reward.CardType);
        Assert.Equal(1, reward.Quantity);
        Assert.Empty(reward.Sizes);
        Assert.Empty(reward.Tiers);
        Assert.Empty(reward.Tags);
        Assert.Equal(new[] { CardHiddenTag.Freeze }, reward.Keywords);
        Assert.Equal("Freeze skill", reward.FilterSummary);
    }

    [Theory]
    // A word inside a longer word is skipped and a later standalone one still counts:
    // "golden" is not Gold, "toolboxes" is not Tool, "tool." is.
    [InlineData("Get 2 Golden Toolboxes or a Diamond tool.", 2, "Diamond Tool")]
    // One trailing "s" is allowed; a hyphen and a word at the end of the text are boundaries.
    [InlineData("Get a Silver-tier Weapons", 1, "Silver Weapon")]
    // "foodie" and "toolss" are not Food or Tool; the text has no card reward.
    [InlineData("Get a foodie toolss", null, null)]
    public void TryParse_matches_whole_words_with_an_optional_plural_s(
        string resultText,
        int? quantity,
        string? summary
    )
    {
        var reward = EncounterRewardParser.TryParse(resultText);

        Assert.Equal(quantity, reward?.Quantity);
        Assert.Equal(summary, reward?.FilterSummary);
    }

    [Fact]
    public void TryParse_ignores_non_card_player_stat_results()
    {
        var reward = EncounterRewardParser.TryParse("Permanently gain 3 Regen");

        Assert.Null(reward);
    }
}
