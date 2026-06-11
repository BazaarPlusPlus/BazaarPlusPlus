#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public static class RandomHeroPoolStateFactory
{
    public static RandomHeroPoolState Create(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? savedPoolHeroIds
    )
    {
        if (unlockedHeroIds is null)
        {
            throw new ArgumentNullException(nameof(unlockedHeroIds));
        }

        var unlockedHeroIdArray = unlockedHeroIds.ToArray();
        var savedPoolHeroIdArray = savedPoolHeroIds?.ToArray();
        return new RandomHeroPoolState(
            unlockedHeroIdArray,
            savedPoolHeroIdArray ?? unlockedHeroIdArray
        );
    }
}
