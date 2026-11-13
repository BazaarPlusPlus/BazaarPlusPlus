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

    public bool Supported => true;

    public void Prewarm() => CombatReplayRecordingGate.Prewarm();

    // The backend probe is warm here.
    public bool CanRecord() => CombatReplayRecordingGate.Evaluate(_videoDirectoryPath).CanRecord;
}
