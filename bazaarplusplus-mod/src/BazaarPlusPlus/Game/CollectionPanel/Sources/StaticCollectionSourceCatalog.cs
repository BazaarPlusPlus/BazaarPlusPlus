#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal sealed class StaticCollectionSourceCatalog : ICollectionSourceCatalog
{
    public bool TryGetBySourceKey(string sourceKey, out CollectionSourceEntry? entry) =>
        CollectionSourceCatalog.TryGetBySourceKey(sourceKey, out entry);

    public IEnumerable<CollectionSourceEntry> For(CollectionSourceKind kind, EHero effectiveHero) =>
        CollectionSourceCatalog.For(kind, effectiveHero);
}
