#nullable enable
using BazaarPlusPlus.Core.GameState;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// Which corpus hero keys are real heroes. The corpus also keys a "Common" pool and may carry
/// other non-playable keys; recommendations query only these.
/// </summary>
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
