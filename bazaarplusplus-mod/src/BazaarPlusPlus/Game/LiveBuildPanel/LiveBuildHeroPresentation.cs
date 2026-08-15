#nullable enable
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static class LiveBuildHeroPresentation
{
    internal static IReadOnlyList<TenWinHeroBuildCount> SelectHeroBuildCounts(
        TenWinCorpusSummary? summary
    ) =>
        summary is { } value
            ? value
                .HeroBuildCounts.Where(entry =>
                    entry.BuildCount > 0 && HeroVisual.IsPlayableHero(entry.Hero)
                )
                .ToArray()
            : Array.Empty<TenWinHeroBuildCount>();

    internal static string DisplayName(string? heroId)
    {
        if (!TheDragonsHeroIdentity.IsAlias(heroId))
            return heroId ?? string.Empty;

        return TheDragonsHeroIdentity.TryResolve(heroId, out var hero)
            ? TheDragonsHeroIdentity.ResolveDisplayName(hero)
            : TheDragonsHeroIdentity.FallbackDisplayName;
    }
}
