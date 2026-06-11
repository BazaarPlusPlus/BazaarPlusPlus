#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal sealed class CollectionSourceRosterItem
{
    public CollectionSourceRosterItem(CollectionSourceEntry entry, bool breakAfter)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        BreakAfter = breakAfter;
    }

    public CollectionSourceEntry Entry { get; }

    public bool BreakAfter { get; }
}

internal static class CollectionSourceRoster
{
    public const string GeneralistGroup = "generalist";
    public const string TierSpecialistGroup = "tier-specialist";

    public static IReadOnlyList<CollectionSourceRosterItem> Build(
        IEnumerable<CollectionSourceEntry> entries
    )
    {
        var sorted = entries.ToList();
        sorted.Sort(CompareEntries);

        var breakIndexes = new HashSet<int>();
        MarkLastGroupEntry(sorted, GeneralistGroup, breakIndexes);
        MarkLastGroupEntry(sorted, TierSpecialistGroup, breakIndexes);

        var result = new List<CollectionSourceRosterItem>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
            result.Add(new CollectionSourceRosterItem(sorted[i], breakIndexes.Contains(i)));
        return result;
    }

    internal static int CompareEntries(CollectionSourceEntry a, CollectionSourceEntry b)
    {
        var byGroup = a.GroupDisplayIndex.CompareTo(b.GroupDisplayIndex);
        if (byGroup != 0)
            return byGroup;

        var byOrder = a.Order.CompareTo(b.Order);
        if (byOrder != 0)
            return byOrder;

        var byName = string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        if (byName != 0)
            return byName;

        return string.Compare(a.SourceKey, b.SourceKey, StringComparison.Ordinal);
    }

    private static void MarkLastGroupEntry(
        IReadOnlyList<CollectionSourceEntry> entries,
        string group,
        HashSet<int> breakIndexes
    )
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(entries[i].Group, group, StringComparison.Ordinal))
                continue;
            breakIndexes.Add(i);
            return;
        }
    }
}
