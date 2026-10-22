#nullable enable
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.HistoryPanel;

// A stored hero id as the game names it; either The Dragons alias shows the canonical name.
internal static class HistoryPanelHeroNames
{
    internal static string DisplayName(string? heroId)
    {
        return TheDragonsHeroIdentity.TryResolve(heroId, out var hero)
                ? TheDragonsHeroIdentity.ResolveDisplayName(hero)
            : TheDragonsHeroIdentity.IsAlias(heroId) ? TheDragonsHeroIdentity.FallbackDisplayName
            : heroId ?? string.Empty;
    }
}
