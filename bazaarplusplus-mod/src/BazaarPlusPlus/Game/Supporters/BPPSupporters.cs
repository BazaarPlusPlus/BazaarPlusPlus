#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporters
{
    private static readonly object SyncRoot = new();
    private static readonly int AttributionShuffleSeed = Environment.TickCount;
    private static int _attributionCursor;

    public static IReadOnlyList<BPPSupporterSample> SampleMany(int count)
    {
        lock (SyncRoot)
        {
            var samples = BPPSupporterSampler.SampleMany(
                BPPSupporterCatalog.GetCurrentEntries(),
                count,
                _attributionCursor,
                AttributionShuffleSeed
            );
            _attributionCursor += count;
            return samples;
        }
    }

    public static BPPSupporterSample Sample()
    {
        return BPPSupporterSampler.Sample(
            BPPSupporterCatalog.GetCurrentEntries(),
            () => UnityEngine.Random.value
        );
    }
}
