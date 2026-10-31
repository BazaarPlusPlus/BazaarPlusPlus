#nullable enable

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The Mobile UI shortcuts' rule beyond the HUD's own on the Mono host (TouchHudMount): they hide
/// while an end-of-run screenshot suppresses BPP chrome (BppUiChromeSuppression), as the desktop
/// dock buttons they replace did. Main thread.
/// </summary>
internal static class TouchShortcutSuppression
{
    private static int _count;

    internal static bool Shown() => _count == 0;

    internal static IDisposable Begin()
    {
        _count++;
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
