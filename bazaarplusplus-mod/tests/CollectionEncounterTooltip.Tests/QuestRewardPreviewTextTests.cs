#nullable enable
using BazaarPlusPlus.Patches.Tooltips;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class QuestRewardPreviewTextTests
{
    [Fact]
    public void AppendRewardPreview_adds_passive_reward_on_smaller_nested_line()
    {
        var text = BppQuestRewardPreviewText.AppendRewardPreview(
            "Sell 20 Food",
            "This has +1 Multicast",
            string.Empty
        );

        Assert.Equal("Sell 20 Food\n<size=76%>This has +1 Multicast</size>", text);
    }

    [Fact]
    public void AppendRewardPreview_uses_active_reward_when_passive_is_empty()
    {
        var text = BppQuestRewardPreviewText.AppendRewardPreview(
            "Sell 10 Food",
            string.Empty,
            "This item's Cooldown is reduced by 2 seconds"
        );

        Assert.Equal(
            "Sell 10 Food\n<size=76%>This item's Cooldown is reduced by 2 seconds</size>",
            text
        );
    }

    [Fact]
    public void AppendRewardPreview_scales_native_inline_size_tags()
    {
        var text = BppQuestRewardPreviewText.AppendRewardPreview(
            "Sell 8 Food",
            "This has +<size=120%>50%</size> Crit Chance",
            string.Empty
        );

        Assert.Equal(
            "Sell 8 Food\n<size=76%>This has +<size=91%>50%</size> Crit Chance</size>",
            text
        );
    }

    [Fact]
    public void AppendRewardPreview_does_not_duplicate_matching_rewards_after_scaling()
    {
        var text = BppQuestRewardPreviewText.AppendRewardPreview(
            "Sell 8 Food",
            "This has +<size=120%>50%</size> Crit Chance",
            "This has +<size=120%>50%</size> Crit Chance"
        );

        Assert.Equal(
            "Sell 8 Food\n<size=76%>This has +<size=91%>50%</size> Crit Chance</size>",
            text
        );
    }

    [Fact]
    public void AppendRewardPreview_dedupes_rewards_after_inline_size_scaling()
    {
        var text = BppQuestRewardPreviewText.AppendRewardPreview(
            "Sell 8 Food",
            "This has +<size=122%>50%</size> Crit Chance",
            "This has +<size=123%>50%</size> Crit Chance"
        );

        Assert.Equal(
            "Sell 8 Food\n<size=76%>This has +<size=93%>50%</size> Crit Chance</size>",
            text
        );
    }

    [Fact]
    public void AppendRewardPreview_keeps_original_text_when_reward_is_empty()
    {
        var text = BppQuestRewardPreviewText.AppendRewardPreview(
            "Sell 10 Food",
            string.Empty,
            "   "
        );

        Assert.Equal("Sell 10 Food", text);
    }

    [Theory]
    [InlineData("Trigger <sprite name=Poison> Poison 30 times", "", true)]
    [InlineData("Deal Burn", "<sprite index=4> 6 Burn", true)]
    [InlineData("Sell 10 Food", "This has +1 Multicast", false)]
    public void ContainsInlineSprite_recognizes_tmp_sprite_markup(
        string questText,
        string rewardText,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            BppQuestRewardPreviewText.ContainsInlineSprite(questText, rewardText)
        );
    }
}
