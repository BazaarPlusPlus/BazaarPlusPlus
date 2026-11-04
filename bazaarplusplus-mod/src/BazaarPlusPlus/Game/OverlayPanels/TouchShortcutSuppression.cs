#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

/// <summary>
/// The Mobile UI HUD's chrome rule beyond its own on the Mono host (TouchHudMount): the shortcuts
/// and the combat clock hide while an end-of-run screenshot suppresses BPP chrome
/// (BppUiChromeSuppression), as the desktop dock buttons and bar they replace did. Main thread.
/// </summary>
internal static class TouchShortcutSuppression
{
    private static int _count;

    /// <summary>
    /// Hides the HUD now, the shortcuts and the combat clock (TouchHudMount sets it). The HUD
    /// ticks in the next frame's Update, after a capture of this frame, so <see cref="Begin"/>
    /// hides it itself. From that tick on, <see cref="Shown"/>, the HUD's chrome rule, keeps both
    /// hidden.
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
