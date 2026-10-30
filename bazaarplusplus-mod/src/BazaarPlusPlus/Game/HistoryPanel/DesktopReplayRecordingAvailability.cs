#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.GameInterop.Touch;

namespace BazaarPlusPlus.Game.HistoryPanel;

// The desktop's replay recording: a native encoder plus a video directory, through the shared
// recording gate. A mobile platform (ADR-0024) has no encoder and records no video, as on Android.
internal sealed class DesktopReplayRecordingAvailability : IReplayRecordingAvailability
{
    private readonly string _videoDirectoryPath;

    public DesktopReplayRecordingAvailability(string videoDirectoryPath)
    {
        _videoDirectoryPath = videoDirectoryPath ?? string.Empty;
    }

    public bool Supported => !TouchPlatform.IsMobile;

    // Capture Unity-owned dimensions/FPS and load the desktop native plugin on the UI thread.
    public void Prewarm()
    {
        if (!Supported)
            return;
        ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out _);
        var backend = ReplayVideoBackendPolicy.Current;
        if (backend == ReplayVideoBackend.MacNative)
        {
            MacMetalVideoEncoder.TryGetAvailability(out _);
            return;
        }
        if (backend == ReplayVideoBackend.WindowsNative)
            WindowsMediaFoundationVideoEncoder.TryGetAvailability(out _);
    }

    // The backend probe is warm here.
    public bool CanRecord() =>
        Supported && CombatReplayRecordingGate.Evaluate(_videoDirectoryPath).CanRecord;
}
