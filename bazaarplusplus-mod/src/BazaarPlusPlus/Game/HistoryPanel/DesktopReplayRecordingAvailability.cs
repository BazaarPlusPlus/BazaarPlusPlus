#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;

namespace BazaarPlusPlus.Game.HistoryPanel;

// The desktop's replay recording: a native encoder plus a video directory, through the shared
// recording gate.
internal sealed class DesktopReplayRecordingAvailability : IReplayRecordingAvailability
{
    private readonly string _videoDirectoryPath;

    public DesktopReplayRecordingAvailability(string videoDirectoryPath)
    {
        _videoDirectoryPath = videoDirectoryPath ?? string.Empty;
    }

    // Capture Unity-owned dimensions/FPS and load the desktop native plugin on the UI thread.
    public void Prewarm()
    {
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
    public bool CanRecord() => CombatReplayRecordingGate.Evaluate(_videoDirectoryPath).CanRecord;
}
