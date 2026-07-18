#nullable enable
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.Game.EventPreview;

internal static class EncounterHeroEligibility
{
    public static bool Matches(IReadOnlyCollection<EHero> stepHeroes, EHero? currentHero)
    {
        if (currentHero == null || stepHeroes.Count == 0)
            return true;
        return Contains(stepHeroes, EHero.Common) || Contains(stepHeroes, currentHero.Value);
    }

    private static bool Contains(IReadOnlyCollection<EHero> values, EHero target)
    {
        foreach (var value in values)
            if (value == target)
                return true;
        return false;
    }
}
