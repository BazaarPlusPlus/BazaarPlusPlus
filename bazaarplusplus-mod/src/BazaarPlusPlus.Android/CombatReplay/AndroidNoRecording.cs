using BazaarPlusPlus.Game.CombatReplay;

namespace BazaarPlusPlus.Android.CombatReplay;

/// <summary>
/// The Android host records no replay video: it ships no encoder, so History shows the record
/// action unavailable with the desktop's own reason.
/// </summary>
internal sealed class AndroidNoRecording : IReplayRecordingAvailability
{
    public static AndroidNoRecording Instance { get; } = new();

    public void Prewarm() { }

    public bool CanRecord() => false;
}
