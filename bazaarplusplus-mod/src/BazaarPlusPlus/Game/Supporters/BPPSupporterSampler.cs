#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterSampler
{
    private const int AttributionRowWindow = 4;
    private const int LongNameCharThreshold = 7;

    internal static IReadOnlyList<BPPSupporterSample> SampleMany(
        IReadOnlyList<BPPSupporterEntry> entries,
        int count,
        int startIndex,
        int shuffleSeed
    )
    {
        if (entries == null || count <= 0)
            return Array.Empty<BPPSupporterSample>();

        var shuffled = BuildShuffledBag(entries, shuffleSeed);
        if (shuffled.Count == 0)
            return Array.Empty<BPPSupporterSample>();

        var samples = new List<BPPSupporterSample>(Math.Min(count, shuffled.Count));
        var cursor = PositiveModulo(startIndex, shuffled.Count);
        while (samples.Count < count && samples.Count < shuffled.Count)
        {
            var entry = shuffled[cursor];
            samples.Add(new BPPSupporterSample { Name = entry.Name.Trim(), Tier = entry.Tier });
            cursor = (cursor + 1) % shuffled.Count;
        }

        return samples;
    }

    internal static BPPSupporterSample Sample(
        IReadOnlyList<BPPSupporterEntry> entries,
        Func<float> randomValue
    )
    {
        if (entries == null || entries.Count == 0)
            return NoSample();

        var buckets = entries
            .Where(IsRenderable)
            .GroupBy(entry => entry.Tier)
            .Select(group => new TierBucket { Tier = group.Key, Entries = group.ToList() })
            .Where(bucket => bucket.Entries.Count > 0)
            .ToList();
        if (buckets.Count == 0)
            return NoSample();

        var selectedBucket = PickWeighted(
            buckets,
            bucket => ResolveTierWeight(bucket.Tier),
            randomValue
        );
        if (selectedBucket == null)
            return NoSample();

        var selectedEntry = PickWeighted(selectedBucket.Entries, _ => 1f, randomValue);
        if (selectedEntry == null)
            return NoSample();

        return new BPPSupporterSample
        {
            Name = selectedEntry.Name.Trim(),
            Tier = selectedEntry.Tier,
        };
    }

    private static BPPSupporterSample NoSample()
    {
        return new BPPSupporterSample { Name = string.Empty, Tier = 0 };
    }

    private static bool IsRenderable(BPPSupporterEntry? entry)
    {
        return entry != null && !string.IsNullOrWhiteSpace(entry.Name) && entry.Tier > 0;
    }

    private static int ResolveTierWeight(int tier)
    {
        return tier switch
        {
            4 => 9,
            3 => 3,
            2 => 2,
            _ => 1,
        };
    }

    private static IReadOnlyList<BPPSupporterEntry> BuildShuffledBag(
        IReadOnlyList<BPPSupporterEntry> entries,
        int shuffleSeed
    )
    {
        var unique = new Dictionary<string, BPPSupporterEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!IsRenderable(entry))
                continue;

            var name = entry.Name.Trim();
            if (!unique.TryGetValue(name, out var existing) || entry.Tier > existing.Tier)
                unique[name] = new BPPSupporterEntry { Name = name, Tier = entry.Tier };
        }

        var shuffled = unique
            .Values.OrderBy(entry => StableHash(entry.Name, entry.Tier, shuffleSeed))
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return DisperseLongNames(shuffled);
    }

    private static IReadOnlyList<BPPSupporterEntry> DisperseLongNames(
        IReadOnlyList<BPPSupporterEntry> entries
    )
    {
        if (entries.Count <= AttributionRowWindow)
            return entries;

        var longNames = new List<BPPSupporterEntry>();
        var shortNames = new Queue<BPPSupporterEntry>();
        foreach (var entry in entries)
        {
            if (IsLongAttributionName(entry))
                longNames.Add(entry);
            else
                shortNames.Enqueue(entry);
        }

        if (longNames.Count <= 1)
            return entries;

        var arranged = new BPPSupporterEntry?[entries.Count];
        if (longNames.Count <= entries.Count / AttributionRowWindow)
        {
            for (var index = 0; index < longNames.Count; index++)
                arranged[index * AttributionRowWindow] = longNames[index];
        }
        else
        {
            for (var index = 0; index < longNames.Count; index++)
            {
                var slot = Math.Min(
                    entries.Count - 1,
                    (int)Math.Floor(index * (double)entries.Count / longNames.Count)
                );
                while (arranged[slot] != null)
                    slot = (slot + 1) % entries.Count;
                arranged[slot] = longNames[index];
            }
        }

        for (var index = 0; index < arranged.Length; index++)
        {
            if (arranged[index] == null && shortNames.Count > 0)
                arranged[index] = shortNames.Dequeue();
        }

        return arranged.Select(entry => entry!).ToList();
    }

    private static int PositiveModulo(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static uint StableHash(string name, int tier, int seed)
    {
        unchecked
        {
            var hash = 2166136261u;
            hash = Mix(hash, (uint)seed);
            hash = Mix(hash, (uint)tier);
            foreach (var ch in name)
                hash = Mix(hash, char.ToUpperInvariant(ch));
            return hash;
        }
    }

    private static bool IsLongAttributionName(BPPSupporterEntry entry) =>
        entry.Name.Trim().Length > LongNameCharThreshold;

    private static uint Mix(uint hash, uint value)
    {
        unchecked
        {
            hash ^= value;
            return hash * 16777619u;
        }
    }

    private static T? PickWeighted<T>(
        IReadOnlyList<T> items,
        Func<T, float> weightSelector,
        Func<float> randomValue
    )
        where T : class
    {
        if (items == null || items.Count == 0)
            return null;

        var totalWeight = 0f;
        for (var i = 0; i < items.Count; i++)
            totalWeight += Math.Max(0f, weightSelector(items[i]));

        if (totalWeight <= 0f)
            return null;

        var roll = randomValue() * totalWeight;
        for (var i = 0; i < items.Count; i++)
        {
            roll -= Math.Max(0f, weightSelector(items[i]));
            if (roll <= 0f)
                return items[i];
        }

        return items[items.Count - 1];
    }

    private sealed class TierBucket
    {
        public int Tier { get; init; }

        public IReadOnlyList<BPPSupporterEntry> Entries { get; init; } =
            Array.Empty<BPPSupporterEntry>();
    }
}
