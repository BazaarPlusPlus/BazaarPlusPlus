#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// Folds the merchants of one catalog group whose members differ in a single setting: the
// generalists, and the tier, size and other-hero merchants. Merchants that sell from any hero and
// tag or type merchants each sell their own theme and stay alone; trainers never fold. Groups take
// the roster position of their first member, and members keep roster order.
public static class CollectionSourceFold
{
    private static readonly HashSet<string> FoldedMerchantGroups = new(StringComparer.Ordinal)
    {
        "generalist",
        "tier-specialist",
        "size-specialist",
        "other-hero",
    };

    // The catalog group a source folds by, or null when it stays alone.
    public static string? KeyFor(CollectionSourceEntry source) =>
        source.Kind == CollectionSourceKind.Merchant && FoldedMerchantGroups.Contains(source.Group)
            ? source.Group
            : null;

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
