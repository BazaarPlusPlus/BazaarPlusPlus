#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Sources;

namespace BazaarPlusPlus.Game.CollectionPanel;

public sealed class CollectionSourceOfferPoolCache : ICollectionOfferPoolResolver
{
    private readonly Dictionary<string, CollectionSourceOfferPoolResult> _cache = new(
        StringComparer.Ordinal
    );

    public CollectionSourceOfferPoolResult GetOrResolve(
        CollectionSourceEntry source,
        CardHero effectiveHero,
        IReadOnlyList<CollectionCardVm> catalogCards
    )
    {
        var key = BuildKey(source, effectiveHero);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = CollectionSourceOfferPoolResolver.Resolve(source, effectiveHero, catalogCards);
        if (result.Status == CollectionSourceOfferPoolStatus.Ready)
            _cache[key] = result;
        return result;
    }

    public string BuildKey(CollectionSourceEntry source, CardHero effectiveHero) =>
        CollectionSourceOfferPoolCacheKey.Build(source, effectiveHero);

    public void Clear() => _cache.Clear();
}
