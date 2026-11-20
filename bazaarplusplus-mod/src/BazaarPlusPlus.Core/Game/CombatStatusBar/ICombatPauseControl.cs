#nullable enable
namespace BazaarPlusPlus.Game.CombatStatusBar;

public interface ICombatPauseControl
{
    bool IsAvailable { get; }

    /// <summary>The game may settle on a different pause state from the one requested.</summary>
    bool TrySetPaused(bool paused, out bool isPaused);
}
