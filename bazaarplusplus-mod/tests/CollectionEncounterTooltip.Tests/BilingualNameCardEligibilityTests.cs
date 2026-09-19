using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.BilingualItemNames;
using Xunit;

namespace EncounterTooltip.Tests;

public sealed class BilingualNameCardEligibilityTests
{
    [Fact]
    public void Supports_existing_item_and_event_names()
    {
        Assert.True(BilingualNameCardEligibility.IsSupported(ECardType.Item));
        Assert.True(BilingualNameCardEligibility.IsSupported(ECardType.EventEncounter));
    }

    [Fact]
    public void Supports_skill_and_reward_names()
    {
        Assert.True(BilingualNameCardEligibility.IsSupported(ECardType.Skill));
        Assert.True(BilingualNameCardEligibility.IsSupported(ECardType.EncounterStep));
    }

    [Fact]
    public void Supports_monster_and_pedestal_names()
    {
        Assert.True(BilingualNameCardEligibility.IsSupported(ECardType.CombatEncounter));
        Assert.True(BilingualNameCardEligibility.IsSupported(ECardType.PedestalEncounter));
    }

    [Fact]
    public void Leaves_unrequested_card_types_unchanged()
    {
        Assert.False(BilingualNameCardEligibility.IsSupported(ECardType.PvpEncounter));
        Assert.False(BilingualNameCardEligibility.IsSupported(ECardType.SocketEffect));
        Assert.False(BilingualNameCardEligibility.IsSupported(ECardType.PlayerEffect));
    }
}
