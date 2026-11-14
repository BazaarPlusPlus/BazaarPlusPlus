using BazaarPlusPlus.Game.RandomCosmetics;
using Xunit;

namespace BazaarPlusPlus.Tests;

public sealed class RandomCosmeticPoolPrefsTests
{
    [Fact]
    public void The_key_names_category_hero_and_escaped_account()
    {
        Assert.Equal(
            "BPP.RandomCosmeticPool.v1.Boards.Vanessa.acc%2F1",
            RandomCosmeticPoolPrefs.BuildKey("Boards", "Vanessa", "acc/1")
        );
    }

    [Theory]
    [InlineData("Hero8")]
    [InlineData("TheDragons")]
    public void Both_names_of_The_Dragons_share_one_key(string heroEnumName)
    {
        Assert.Equal(
            "BPP.RandomCosmeticPool.v1.HeroSkins.TheDragons.acc",
            RandomCosmeticPoolPrefs.BuildKey("HeroSkins", heroEnumName, "acc")
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void A_missing_account_falls_back_to_the_anonymous_scope(string? accountScope)
    {
        Assert.Equal(
            "BPP.RandomCosmeticPool.v1.Toys.Mak.anonymous",
            RandomCosmeticPoolPrefs.BuildKey("Toys", "Mak", accountScope)
        );
    }

    [Fact]
    public void A_saved_pool_round_trips_without_blanks_or_duplicates()
    {
        var raw = RandomCosmeticPoolPrefs.Serialize(["a", "", "b", "a"]);

        Assert.Equal(["a", "b"], RandomCosmeticPoolPrefs.TryParse(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    public void An_unreadable_value_is_no_saved_pool(string? raw)
    {
        Assert.Null(RandomCosmeticPoolPrefs.TryParse(raw));
    }
}
