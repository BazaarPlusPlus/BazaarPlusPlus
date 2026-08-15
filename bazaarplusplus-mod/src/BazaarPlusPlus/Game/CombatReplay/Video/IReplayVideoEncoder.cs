#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Sealed video sink handed from the Unity capture loop to the background drain.
/// Implementations own an in-process platform encoder.
/// </summary>
internal interface IReplayVideoEncoder : IDisposable
{
    bool WriterFailed { get; }

    ReplayVideoEncoderFailureReasonCode FailureReasonCode { get; }

    void SignalEndOfStream();

    ReplayVideoEncoderCompletionOutcome WaitForCompletion(TimeSpan timeout);
}
