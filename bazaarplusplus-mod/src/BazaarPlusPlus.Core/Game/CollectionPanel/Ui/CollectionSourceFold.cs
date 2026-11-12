#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// Folds the sources whose click shows the same cards into one group. Groups take the roster
// position of their first member, and members keep roster order. A source with no key (its offer
// pool did not resolve) stays alone.
public static class CollectionSourceFold
{
    public static IReadOnlyList<CollectionSourceGroup> Group(
        IReadOnlyList<CollectionSourceOptionViewModel> sources,
        IReadOnlyDictionary<string, CollectionSourceFoldKey> keys
    )
    {
        if (sources == null)
            throw new ArgumentNullException(nameof(sources));
        if (keys == null)
            throw new ArgumentNullException(nameof(keys));

        var members = new List<List<CollectionSourceOptionViewModel>>(sources.Count);
        var byKey =
            new Dictionary<CollectionSourceFoldKey, List<CollectionSourceOptionViewModel>>();
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

// What a source's click shows (CollectionQuery.Run): its offer pool for the effective hero,
// whether it skips the Day gate (a fixed-tier source), and on Skills whether it skips the hero
// filter (a trainer with an all-heroes segment). Two keys are equal when all three are.
public sealed class CollectionSourceFoldKey : IEquatable<CollectionSourceFoldKey>
{
    private readonly HashSet<Guid> _offeredCardIds;
    private readonly bool _suppressDayGate;
    private readonly bool _skipsHeroFilter;
    private readonly int _hash;

    public CollectionSourceFoldKey(
        IReadOnlyCollection<Guid> offeredCardIds,
        bool suppressDayGate,
        bool skipsHeroFilter
    )
    {
        if (offeredCardIds == null)
            throw new ArgumentNullException(nameof(offeredCardIds));
        _offeredCardIds = new HashSet<Guid>(offeredCardIds);
        _suppressDayGate = suppressDayGate;
        _skipsHeroFilter = skipsHeroFilter;
        var hash = _offeredCardIds.Count;
        foreach (var id in _offeredCardIds)
            hash ^= id.GetHashCode();
        _hash = hash ^ (suppressDayGate ? 0x10000 : 0) ^ (skipsHeroFilter ? 0x20000 : 0);
    }

    public static CollectionSourceFoldKey For(
        CollectionSourceEntry source,
        IReadOnlyCollection<Guid> offeredCardIds
    ) =>
        new(
            offeredCardIds,
            source.SuppressDayGate,
            source.Kind == CollectionSourceKind.Trainer && source.HasAllHeroesOfferSegment
        );

    public bool Equals(CollectionSourceFoldKey? other) =>
        other != null
        && _hash == other._hash
        && _suppressDayGate == other._suppressDayGate
        && _skipsHeroFilter == other._skipsHeroFilter
        && _offeredCardIds.SetEquals(other._offeredCardIds);

    public override bool Equals(object? obj) => Equals(obj as CollectionSourceFoldKey);

    public override int GetHashCode() => _hash;
}
