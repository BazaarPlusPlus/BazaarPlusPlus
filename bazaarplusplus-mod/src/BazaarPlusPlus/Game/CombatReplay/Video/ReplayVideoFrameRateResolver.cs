#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class ReplayVideoFrameRateResolver
{
    internal static int Resolve()
    {
        // The native desktop recorders have a fixed 60 fps output contract. The wall-clock pacer
        // repeats the latest GPU surface when Unity does not render a distinct frame in time.
        if (ReplayVideoBackendPolicy.Current == ReplayVideoBackend.MacNative)
            return ReplayVideoCaptureDefaults.MacNativeFps;
        if (ReplayVideoBackendPolicy.Current == ReplayVideoBackend.WindowsNative)
            return ReplayVideoCaptureDefaults.WindowsNativeFps;

        try
        {
            var preferences = PlayerPreferences.Data;
            var requested = preferences.VideoSynchronization
                ? (int)Math.Round(Screen.currentResolution.refreshRateRatio.value)
                : preferences.TargetFramerate;
            return Normalize(requested);
        }
        catch
        {
            return Normalize(Application.targetFrameRate);
        }
    }

    internal static int Normalize(int requestedFrameRate) =>
        requestedFrameRate > 0
            ? Math.Min(requestedFrameRate, ReplayVideoCaptureDefaults.MaxFps)
            : ReplayVideoCaptureDefaults.FallbackFps;
}
