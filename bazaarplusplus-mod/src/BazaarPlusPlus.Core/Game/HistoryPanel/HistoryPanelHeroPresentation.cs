#nullable enable
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.HistoryPanel;

// The Runs hero filter over stored hero ids; display names come from the host's native names.
public static class HistoryPanelHeroPresentation
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
        TheDragonsHeroId.CanonicalId,
    };

    public static IReadOnlyList<string> RunFilterHeroIds => RunFilterRoster;

    public static bool IsSelected(string? selectedHero, string heroId) =>
        string.Equals(selectedHero, heroId, StringComparison.OrdinalIgnoreCase);
}
