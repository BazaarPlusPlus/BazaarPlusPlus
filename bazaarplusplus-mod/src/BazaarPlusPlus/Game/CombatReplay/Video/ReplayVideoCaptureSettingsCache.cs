#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal readonly record struct ReplayVideoCaptureSettings(int Width, int Height, int Fps);

internal static class ReplayVideoCaptureSettingsCache
{
    // Unity-owned state must only be sampled by callers already running on the main thread.
    internal static bool TryCaptureCurrent(out ReplayVideoCaptureSettings settings)
    {
        if (
            !ReplayVideoCaptureDefaults.TryGetEvenDimensions(
                Screen.width,
                Screen.height,
                out var width,
                out var height
            )
        )
        {
            settings = default;
            return false;
        }

        settings = new ReplayVideoCaptureSettings(
            width,
            height,
            ReplayVideoFrameRateResolver.Resolve()
        );
        return true;
    }
}
