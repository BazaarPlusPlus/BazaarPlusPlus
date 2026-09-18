#nullable enable
using BazaarPlusPlus.GameInterop.Heroes;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelHeroPresentation
{
    private static readonly string[] RunFilterRoster =
    {
        "Vanessa",
        "Pygmalien",
        "Dooley",
        "Mak",
        "Jules",
        "Karnok",
        "Stelle",
        TheDragonsHeroIdentity.CanonicalId,
    };

    internal static IReadOnlyList<string> RunFilterHeroIds => RunFilterRoster;

    internal static bool IsSelected(string? selectedHero, string heroId) =>
        TheDragonsHeroIdentity.AreEquivalent(selectedHero, heroId)
        || string.Equals(selectedHero, heroId, StringComparison.OrdinalIgnoreCase);

    internal static string DisplayName(string? heroId)
    {
        return TheDragonsHeroIdentity.TryResolve(heroId, out var hero)
                ? TheDragonsHeroIdentity.ResolveDisplayName(hero)
            : TheDragonsHeroIdentity.IsAlias(heroId) ? TheDragonsHeroIdentity.FallbackDisplayName
            : heroId ?? string.Empty;
    }

    internal static string? CanonicalFilterId(string? heroId) =>
        TheDragonsHeroIdentity.IsAlias(heroId) ? TheDragonsHeroIdentity.CanonicalId
        : string.IsNullOrEmpty(heroId) ? null
        : heroId;
}
