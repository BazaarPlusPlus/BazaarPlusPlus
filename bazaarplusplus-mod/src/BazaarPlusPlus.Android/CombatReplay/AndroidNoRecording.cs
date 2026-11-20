using BazaarPlusPlus.Game.CombatReplay;

namespace BazaarPlusPlus.Android.CombatReplay;

internal sealed class AndroidNoRecording : IReplayRecordingAvailability
{
    public static AndroidNoRecording Instance { get; } = new();

    public bool Supported => false;

    public void Prewarm() { }

    public bool CanRecord() => false;
}
