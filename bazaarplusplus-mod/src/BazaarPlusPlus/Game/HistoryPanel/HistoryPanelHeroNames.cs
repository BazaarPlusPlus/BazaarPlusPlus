#nullable enable
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.HistoryPanel;

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
