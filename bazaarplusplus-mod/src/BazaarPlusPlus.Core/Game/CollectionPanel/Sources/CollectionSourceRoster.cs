#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

public static class CollectionSourceRoster
{
    public static IReadOnlyList<CollectionSourceEntry> Build(
        IEnumerable<CollectionSourceEntry> entries
    )
    {
        var sorted = entries.ToList();
        sorted.Sort(CompareEntries);
        return sorted;
    }

    public static int CompareEntries(CollectionSourceEntry a, CollectionSourceEntry b)
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
}
