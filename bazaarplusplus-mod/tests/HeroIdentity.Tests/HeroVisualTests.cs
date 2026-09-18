using BazaarPlusPlus.GameInterop.Heroes;
using Xunit;

namespace HeroIdentity.Tests;

public sealed class HeroVisualTests
{
    [Theory]
    [InlineData("Hero8")]
    [InlineData(" hero8 ")]
    [InlineData("TheDragons")]
    [InlineData(" THEDRAGONS ")]
    public void Both_aliases_share_the_playable_dragons_badge(string alias)
    {
        var style = HeroVisual.Resolve(alias);

        Assert.Equal("DRA", style.ShortCode);
        Assert.Equal(45f / 255f, style.Background.r, precision: 6);
        Assert.Equal(210f / 255f, style.Background.g, precision: 6);
        Assert.Equal(208f / 255f, style.Background.b, precision: 6);
        Assert.True(HeroVisual.IsPlayableHero(alias));
    }

    [Fact]
    public void Existing_hero_visuals_remain_unchanged()
    {
        var style = HeroVisual.Resolve("Vanessa");

        Assert.Equal("VAN", style.ShortCode);
        Assert.True(HeroVisual.IsPlayableHero("Vanessa"));
        Assert.False(HeroVisual.IsPlayableHero("Common"));
        Assert.False(HeroVisual.IsPlayableHero(null));
    }
}
