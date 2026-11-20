#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.CombatReplay.Video;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class DesktopReplayRecordingAvailability : IReplayRecordingAvailability
{
    private readonly string _videoDirectoryPath;

    public DesktopReplayRecordingAvailability(string videoDirectoryPath)
    {
        _videoDirectoryPath = videoDirectoryPath ?? string.Empty;
    }

    public bool Supported => true;

    public void Prewarm() => CombatReplayRecordingGate.Prewarm();

    public bool CanRecord() => CombatReplayRecordingGate.Evaluate(_videoDirectoryPath).CanRecord;
}
