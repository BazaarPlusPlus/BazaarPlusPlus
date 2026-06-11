#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

public sealed class RandomHeroPoolSelector
{
    public IReadOnlyList<string> BuildCandidateHeroIds(
        IEnumerable<string> unlockedHeroIds,
        IEnumerable<string>? savedPoolHeroIds
    )
    {
        if (unlockedHeroIds is null)
        {
            throw new ArgumentNullException(nameof(unlockedHeroIds));
        }

        var unlockedHeroIdArray = unlockedHeroIds
            .Where(heroId => !string.IsNullOrWhiteSpace(heroId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unlockedHeroIdArray.Length == 0)
        {
            throw new ArgumentException(
                "Random hero pool requires at least one unlocked hero.",
                nameof(unlockedHeroIds)
            );
        }

        var selectedHeroIds = new HashSet<string>(
            RandomHeroPoolPreferences.Sanitize(unlockedHeroIdArray, savedPoolHeroIds),
            StringComparer.Ordinal
        );
        return unlockedHeroIdArray.Where(selectedHeroIds.Contains).ToArray();
    }

    public string SelectHero(IReadOnlyList<string> candidateHeroIds, int randomIndex)
    {
        if (candidateHeroIds is null)
        {
            throw new ArgumentNullException(nameof(candidateHeroIds));
        }

        if (candidateHeroIds.Count == 0)
        {
            throw new InvalidOperationException("Random hero pool cannot be empty.");
        }

        if ((uint)randomIndex >= (uint)candidateHeroIds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(randomIndex));
        }

        return candidateHeroIds[randomIndex];
    }
}
