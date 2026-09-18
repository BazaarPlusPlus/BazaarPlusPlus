#nullable enable
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static class LiveBuildHeroPresentation
{
    internal static string DisplayName(string? heroId)
    {
        if (!TheDragonsHeroIdentity.IsAlias(heroId))
            return heroId ?? string.Empty;

        return TheDragonsHeroIdentity.TryResolve(heroId, out var hero)
            ? TheDragonsHeroIdentity.ResolveDisplayName(hero)
            : TheDragonsHeroIdentity.FallbackDisplayName;
    }
}
