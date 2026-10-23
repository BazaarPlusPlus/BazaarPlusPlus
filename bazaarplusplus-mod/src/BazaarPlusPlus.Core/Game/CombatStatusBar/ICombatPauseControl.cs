#nullable enable
namespace BazaarPlusPlus.Game.CombatStatusBar;

/// <summary>The host's pause switch for combat playback.</summary>
public interface ICombatPauseControl
{
    /// <summary>False while the game has no service that can pause.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Requests <paramref name="paused"/> and reports the pause state the game settled on;
    /// returns false, leaving <paramref name="isPaused"/> unset, when no service can pause.
    /// </summary>
    bool TrySetPaused(bool paused, out bool isPaused);
}
