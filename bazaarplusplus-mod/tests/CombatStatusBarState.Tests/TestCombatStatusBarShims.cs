using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CombatStatusBar;

namespace BazaarPlusPlus.Tests;

internal sealed class TestGameStateProbe : IGameStateProbe
{
    public bool Result { get; set; }

    public bool ComputeIsInGameRun() => Result;
}

internal sealed class TestPauseControl : ICombatPauseControl
{
    public bool? Paused { get; set; }

    public bool IsAvailable => Paused.HasValue;

    public bool TrySetPaused(bool paused, out bool isPaused)
    {
        isPaused = paused;
        if (!Paused.HasValue)
            return false;

        Paused = paused;
        return true;
    }
}
