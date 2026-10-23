using BazaarPlusPlus.Core.Cards;
using Xunit;

namespace EventPreview.Tests;

public sealed class EncounterHeroEligibilityTests
{
    [Fact]
    public void Includes_common_steps_for_a_specific_hero()
    {
        Assert.True(EncounterHeroEligibility.Matches(new[] { CardHero.Common }, CardHero.Stelle));
    }

    [Fact]
    public void Includes_steps_for_the_current_hero()
    {
        Assert.True(
            EncounterHeroEligibility.Matches(
                new[] { CardHero.Vanessa, CardHero.Dooley, CardHero.Karnok },
                CardHero.Dooley
            )
        );
    }

    [Fact]
    public void Excludes_steps_for_other_heroes()
    {
        Assert.False(EncounterHeroEligibility.Matches(new[] { CardHero.Mak }, CardHero.Stelle));
    }

    [Fact]
    public void Keeps_all_steps_when_current_hero_is_unknown()
    {
        Assert.True(EncounterHeroEligibility.Matches(new[] { CardHero.Mak }, null));
    }
}
