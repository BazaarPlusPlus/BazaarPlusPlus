#nullable enable
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

// The analyzer's Common pool key has no playable hero.
public static class LiveBuildPlayableHero
{
    // An explicit list rather than Enum.TryParse, which would also accept numeric strings.
    public static bool IsPlayable(string? heroId) =>
        TheDragonsHeroId.IsAlias(heroId)
        || heroId?.Trim() switch
        {
            "Vanessa" or "Pygmalien" or "Dooley" or "Mak" or "Jules" or "Karnok" or "Stelle" =>
                true,
            _ => false,
        };
}
