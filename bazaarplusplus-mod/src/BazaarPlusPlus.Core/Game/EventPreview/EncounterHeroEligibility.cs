#nullable enable
using BazaarPlusPlus.Core.Cards;

namespace BazaarPlusPlus.Game.EventPreview;

public static class EncounterHeroEligibility
{
    public static bool Matches(IReadOnlyCollection<CardHero> stepHeroes, CardHero? currentHero)
    {
        if (currentHero == null || stepHeroes.Count == 0)
            return true;
        return Contains(stepHeroes, CardHero.Common) || Contains(stepHeroes, currentHero.Value);
    }

    private static bool Contains(IReadOnlyCollection<CardHero> values, CardHero target)
    {
        foreach (var value in values)
            if (value == target)
                return true;
        return false;
    }
}
