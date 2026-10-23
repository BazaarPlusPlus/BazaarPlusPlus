using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Game.CombatStatusBar;

namespace BazaarPlusPlus.Tests;

internal sealed class TestGameStateProbe : IGameStateProbe
{
    public bool Result { get; set; }

    public bool ComputeIsInGameRun() => Result;
}

/// <summary>Stands in for the game's service manager; a null <see cref="Paused"/> means none exists.</summary>
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
