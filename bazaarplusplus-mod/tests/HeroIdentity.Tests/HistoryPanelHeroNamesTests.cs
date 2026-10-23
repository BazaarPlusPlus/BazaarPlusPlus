using BazaarPlusPlus.Game.HistoryPanel;
using Xunit;

namespace HeroIdentity.Tests;

public sealed class HistoryPanelHeroNamesTests
{
    [Theory]
    [InlineData("Hero8")]
    [InlineData("TheDragons")]
    public void Either_The_Dragons_alias_displays_the_canonical_native_name(string heroId)
    {
        Assert.Equal("The Dragons", HistoryPanelHeroNames.DisplayName(heroId));
    }
}
