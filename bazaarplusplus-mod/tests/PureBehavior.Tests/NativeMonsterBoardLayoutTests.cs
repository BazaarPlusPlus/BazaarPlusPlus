using BazaarPlusPlus.GameInterop.MonsterBoardPreview;
using Xunit;

public sealed class NativeMonsterBoardLayoutTests
{
    [Theory]
    [InlineData(1600, 170)]
    [InlineData(900, 120)]
    [InlineData(700, 300)]
    public void Item_only_boards_preserve_native_aspect_and_reserve_badge_space(
        float width,
        float height
    )
    {
        var layout = NativeMonsterBoardLayout.Calculate(
            width,
            height,
            2600,
            550,
            0,
            0,
            itemsOnly: true,
            itemFooterHeight: 23,
            itemTopInset: 3
        );
        Assert.True(2600 * layout.BoardScale <= width + .001f);
        Assert.True(550 * layout.BoardScale + 26 <= height + .001f);
        Assert.Equal(Math.Min(width / 2600, (height - 26) / 550), layout.BoardScale);
    }

    [Fact]
    public void Adding_skills_keeps_cards_and_skill_icons_the_same_size_and_expands_scroll_content()
    {
        var few = NativeMonsterBoardLayout.Calculate(1000, 300, 2600, 550, 600, 200);
        var many = NativeMonsterBoardLayout.Calculate(1000, 300, 2600, 550, 8000, 200);
        Assert.Equal(few.BoardScale, many.BoardScale);
        Assert.Equal(few.SkillScale, many.SkillScale);
        Assert.Equal(1000f, few.SkillContentWidth);
        Assert.True(many.SkillContentWidth > 1000);
    }

    [Fact]
    public void Board_dimensions_do_not_change_skill_scale()
    {
        var small = NativeMonsterBoardLayout.Calculate(1000, 300, 2000, 500, 3000, 200);
        var large = NativeMonsterBoardLayout.Calculate(1000, 300, 3000, 1000, 3000, 200);
        Assert.NotEqual(small.BoardScale, large.BoardScale);
        Assert.Equal(small.SkillScale, large.SkillScale);
    }
}
