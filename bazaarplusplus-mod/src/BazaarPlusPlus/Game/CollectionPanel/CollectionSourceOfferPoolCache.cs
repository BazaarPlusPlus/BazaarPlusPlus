#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionSourceOfferPoolCache
{
    private readonly Dictionary<string, CollectionSourceOfferPoolResult> _cache = new(
        StringComparer.Ordinal
    );

    public CollectionSourceOfferPoolResult GetOrResolve(
        CollectionSourceEntry source,
        EHero? selectedHero,
        IReadOnlyList<CollectionCardVm> catalogCards
    )
    {
        var key = BuildKey(source, selectedHero);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = CollectionSourceOfferPoolResolver.Resolve(source, selectedHero, catalogCards);
        if (result.Status == CollectionSourceOfferPoolStatus.Ready)
            _cache[key] = result;
        return result;
    }

    public string BuildKey(CollectionSourceEntry source, EHero? selectedHero) =>
        CollectionSourceOfferPoolCacheKey.Build(source, selectedHero);

    public void Clear() => _cache.Clear();
}
