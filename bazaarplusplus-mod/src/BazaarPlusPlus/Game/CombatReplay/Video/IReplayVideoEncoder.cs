#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal interface IReplayVideoEncoder : IDisposable
{
    bool WriterFailed { get; }

    void SignalEndOfStream();

    ReplayVideoEncoderCompletionOutcome WaitForCompletion(TimeSpan timeout);
}
