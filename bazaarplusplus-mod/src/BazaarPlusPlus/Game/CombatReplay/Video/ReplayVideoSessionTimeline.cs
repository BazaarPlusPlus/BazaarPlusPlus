#nullable enable
using System.Diagnostics;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

/// <summary>
/// Stopwatch timestamps of a capture session's phases, split so a slow recording names its slow
/// phase. A timestamp of 0 marks a phase the session never reached, which reports no duration.
/// These are diagnostic wall-clock spans: the video timeline starts at the first frame, when
/// <see cref="WallClockCfrPacer"/> locks its clock, so none of them is the video's length.
/// </summary>
internal readonly record struct ReplayVideoSessionTimeline(
    long ConstructedTimestamp,
    long StartedTimestamp,
    long FirstFrameTimestamp,
    long FinalizedTimestamp
)
{
    /// <summary>Session construction until the native encoder has started.</summary>
    internal long? InitMs => Between(ConstructedTimestamp, StartedTimestamp);

    /// <summary>Encoder start until the first frame reached the encoder.</summary>
    internal long? FirstFrameWaitMs => Between(StartedTimestamp, FirstFrameTimestamp);

    /// <summary>First frame until the session was finalized: the recorded span.</summary>
    internal long? CaptureMs => Between(FirstFrameTimestamp, FinalizedTimestamp);

    /// <summary>Finalization until the encoder drain completed.</summary>
    internal long? DrainMs(long completedTimestamp) =>
        Between(FinalizedTimestamp, completedTimestamp);

    private static long? Between(long start, long end)
    {
        if (start == 0 || end == 0)
            return null;
        return (long)Math.Max(0, (end - start) * 1000d / Stopwatch.Frequency);
    }
}
