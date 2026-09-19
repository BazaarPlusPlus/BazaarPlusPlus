using BazaarPlusPlus.Game.CombatStatusBar;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class CombatStatusBarLayoutTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void FillsSpaceBelowPortraitAtEveryDisplayScale(float scale)
    {
        Assert.True(
            CombatStatusBarLayout.TryPlace(
                850f * scale,
                1070f * scale,
                63f * scale,
                0f,
                1920f * scale,
                0f,
                1080f * scale,
                scale,
                out var height
            )
        );
        Assert.Equal(60f * scale, height);
    }

    [Fact]
    public void RespectsViewportBottomInsteadOfDrawingIntoLetterbox()
    {
        Assert.True(
            CombatStatusBarLayout.TryPlace(
                850f,
                1070f,
                163f,
                0f,
                1920f,
                100f,
                980f,
                1f,
                out var height
            )
        );
        Assert.Equal(60f, height);
    }

    [Theory]
    [InlineData(-5f, 215f, 63f)]
    [InlineData(1800f, 2020f, 63f)]
    [InlineData(850f, 1070f, 20f)]
    [InlineData(850f, 900f, 63f)]
    [InlineData(1070f, 850f, 63f)]
    [InlineData(850f, 1070f, 1200f)]
    [InlineData(float.NaN, 1070f, 63f)]
    [InlineData(850f, float.PositiveInfinity, 63f)]
    public void HidesUnusableOrOffscreenGeometry(float left, float right, float bottom)
    {
        Assert.False(
            CombatStatusBarLayout.TryPlace(left, right, bottom, 0f, 1920f, 0f, 1080f, 1f, out _)
        );
    }
}
