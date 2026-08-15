#nullable enable
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

internal static class RandomHeroPoolHeroIdentity
{
    internal static string Normalize(string? heroId) =>
        TheDragonsHeroIdentity.CanonicalizeForStorage(heroId);

    internal static bool Matches(string? runtimeHeroId, string? selectedHeroId) =>
        string.Equals(
            Normalize(runtimeHeroId),
            Normalize(selectedHeroId),
            StringComparison.Ordinal
        );
}
