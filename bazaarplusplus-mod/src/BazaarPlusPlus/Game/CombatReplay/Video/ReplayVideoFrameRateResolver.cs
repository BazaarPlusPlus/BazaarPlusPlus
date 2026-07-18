#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal static class ReplayVideoFrameRateResolver
{
    internal static int Resolve()
    {
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
