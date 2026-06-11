#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroSkinPool;

internal static class RandomHeroSkinPoolStateFactory
{
    public static RandomHeroSkinPoolState Create(
        IEnumerable<string> availableSkinIds,
        IEnumerable<string>? savedSelectedSkinIds
    )
    {
        if (availableSkinIds == null)
            throw new ArgumentNullException(nameof(availableSkinIds));

        var available = availableSkinIds
            .Where(skinId => !string.IsNullOrWhiteSpace(skinId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (available.Length == 0)
            throw new ArgumentException("Random hero skin pool requires at least one skin.");

        var selected = savedSelectedSkinIds?.ToArray() ?? available;
        return new RandomHeroSkinPoolState(available, selected);
    }
}
