#nullable enable
#if DEBUG
using System.Diagnostics;
#endif

namespace BazaarPlusPlus.Game.CombatReplay.Video;

#if DEBUG
internal sealed class ReplayVideoCopyTimingAccumulator
{
    private readonly long[] _samples;
    private int _count;
    private int _next;

    internal ReplayVideoCopyTimingAccumulator(int capacity = 2048)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new long[capacity];
    }

    internal long P50Microseconds => PercentileMicroseconds(0.50d);

    internal long P95Microseconds => PercentileMicroseconds(0.95d);

    internal long P99Microseconds => PercentileMicroseconds(0.99d);

    private long PercentileMicroseconds(double percentile)
    {
        if (_count == 0)
            return 0;

        var snapshot = new long[_count];
        Array.Copy(_samples, snapshot, _count);
        Array.Sort(snapshot);
        var index = Math.Max(0, (int)Math.Ceiling(snapshot.Length * percentile) - 1);
        return snapshot[index];
    }

    internal void ObserveSince(long startedTimestamp)
    {
        var elapsedTicks = Math.Max(0, Stopwatch.GetTimestamp() - startedTimestamp);
        var microseconds = (long)Math.Ceiling(elapsedTicks * 1_000_000d / Stopwatch.Frequency);
        ObserveMicroseconds(microseconds);
    }

    internal void ObserveMicroseconds(long microseconds)
    {
        microseconds = Math.Max(0, microseconds);
        _samples[_next] = microseconds;
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length)
            _count++;
    }
}
#else
// Every reader of these percentiles is a BppLog.DebugEvent, which compiles out of Release, so a
// Release build keeps no samples: no per-playback buffer, and no copy and sort at finalize.
internal sealed class ReplayVideoCopyTimingAccumulator
{
    internal ReplayVideoCopyTimingAccumulator(int capacity = 2048)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
    }

    internal long P50Microseconds => 0;

    internal long P95Microseconds => 0;

    internal long P99Microseconds => 0;

    internal void ObserveSince(long startedTimestamp) { }

    internal void ObserveMicroseconds(long microseconds) { }
}
#endif
