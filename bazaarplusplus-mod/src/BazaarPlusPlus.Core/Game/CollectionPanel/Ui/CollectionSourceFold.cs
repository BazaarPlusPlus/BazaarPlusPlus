#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// Folds the merchants with one catalog fold value (collection-sources.json "fold", by what the
// merchant filters on: all items, tier, size or hero) into one cell. A source without a fold value
// stays alone. Groups take the roster position of their first member, and members keep roster
// order.
public static class CollectionSourceFold
{
    public static IReadOnlyList<CollectionSourceGroup> Group(
        IReadOnlyList<CollectionSourceOptionViewModel> sources,
        IReadOnlyDictionary<string, string> keys
    )
    {
        if (sources == null)
            throw new ArgumentNullException(nameof(sources));
        if (keys == null)
            throw new ArgumentNullException(nameof(keys));

        var members = new List<List<CollectionSourceOptionViewModel>>(sources.Count);
        var byKey = new Dictionary<string, List<CollectionSourceOptionViewModel>>(
            StringComparer.Ordinal
        );
        foreach (var source in sources)
        {
            if (!keys.TryGetValue(source.SourceKey, out var key))
            {
                members.Add(new List<CollectionSourceOptionViewModel> { source });
                continue;
            }
            if (byKey.TryGetValue(key, out var group))
            {
                group.Add(source);
                continue;
            }
            group = new List<CollectionSourceOptionViewModel> { source };
            byKey[key] = group;
            members.Add(group);
        }

        var groups = new List<CollectionSourceGroup>(members.Count);
        foreach (var group in members)
            groups.Add(new CollectionSourceGroup(group));
        return groups;
    }
}
