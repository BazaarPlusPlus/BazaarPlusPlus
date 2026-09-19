#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

internal readonly struct ReplayAudioCaptureResult
{
    public string WavPath { get; init; }

    public bool Usable { get; init; }

    public ReplayAudioFailureReasonCode FailureReason { get; init; }

    public System.Exception? FailureException { get; init; }
}
