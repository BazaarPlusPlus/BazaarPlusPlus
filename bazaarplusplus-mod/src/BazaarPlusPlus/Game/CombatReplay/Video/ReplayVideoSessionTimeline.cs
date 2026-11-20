#nullable enable
using System.Diagnostics;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

// Zero marks an unreached phase. These wall-clock spans are diagnostic, separate from the timeline that starts at the first frame.
internal readonly record struct ReplayVideoSessionTimeline(
    long ConstructedTimestamp,
    long StartedTimestamp,
    long FirstFrameTimestamp,
    long FinalizedTimestamp
)
{
    internal long? InitMs => Between(ConstructedTimestamp, StartedTimestamp);

    internal long? FirstFrameWaitMs => Between(StartedTimestamp, FirstFrameTimestamp);

    internal long? CaptureMs => Between(FirstFrameTimestamp, FinalizedTimestamp);

    internal long? DrainMs(long completedTimestamp) =>
        Between(FinalizedTimestamp, completedTimestamp);

    private static long? Between(long start, long end)
    {
        if (start == 0 || end == 0)
            return null;
        return (long)Math.Max(0, (end - start) * 1000d / Stopwatch.Frequency);
    }
}
