#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

/// <summary>
/// The Mobile UI shortcuts' rule beyond the HUD's own on the Mono host (TouchHudMount): they hide
/// while an end-of-run screenshot suppresses BPP chrome (BppUiChromeSuppression), as the desktop
/// dock buttons they replace did. Main thread.
/// </summary>
internal static class TouchShortcutSuppression
{
    private static int _count;

    /// <summary>
    /// Hides the HUD now, the shortcuts and the combat clock (TouchHudMount sets it). The HUD
    /// ticks in the next frame's Update, after a capture of this frame, so <see cref="Begin"/>
    /// hides it itself. From that tick on, each part's own rule keeps it hidden: the shortcuts'
    /// <see cref="Shown"/>, and the clock's CombatStatusBar.ShowsTouchClock, whose lease the
    /// same screenshot scope holds (BppUiChromeSuppression).
    /// </summary>
    internal static Action? Hide { get; set; }

    internal static bool Shown() => _count == 0;

    internal static IDisposable Begin()
    {
        _count++;
        Hide?.Invoke();
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _count--;
        }
    }
}
