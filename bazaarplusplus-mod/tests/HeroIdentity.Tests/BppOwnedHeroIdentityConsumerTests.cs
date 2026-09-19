using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.GameInterop.Heroes;
using Xunit;

namespace HeroIdentity.Tests;

public sealed class BppOwnedHeroIdentityConsumerTests
{
    [Theory]
    [InlineData("Hero8")]
    [InlineData("TheDragons")]
    public void Replay_manifest_aliases_resolve_to_current_runtime_hero(string heroId)
    {
        Assert.True(CombatReplayHeroIdentity.TryParse(heroId, out var hero));
        Assert.True(TheDragonsHeroIdentity.IsTheDragons(hero));
    }

    [Fact]
    public void Replay_manifest_alias_degrades_when_runtime_exposes_neither_name()
    {
        Assert.False(CombatReplayHeroIdentity.TryParse("TheDragons", _ => null, out _));
    }

    [Fact]
    public void Replay_manifest_preserves_existing_case_insensitive_non_alias_parsing()
    {
        Assert.True(CombatReplayHeroIdentity.TryParse("vanessa", out var hero));
        Assert.Equal(EHero.Vanessa, hero);
        Assert.False(CombatReplayHeroIdentity.TryParse("UnknownHero", out _));
    }
}
