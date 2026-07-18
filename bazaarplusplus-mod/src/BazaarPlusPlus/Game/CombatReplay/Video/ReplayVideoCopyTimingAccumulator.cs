#nullable enable
using System.Diagnostics;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

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

    internal int SampleCount => _count;

    internal long P95Microseconds
    {
        get
        {
            if (_count == 0)
                return 0;

            var snapshot = new long[_count];
            Array.Copy(_samples, snapshot, _count);
            Array.Sort(snapshot);
            var index = Math.Max(0, (int)Math.Ceiling(snapshot.Length * 0.95d) - 1);
            return snapshot[index];
        }
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
