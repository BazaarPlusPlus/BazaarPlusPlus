#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal sealed class StaticCollectionSourceCatalog : ICollectionSourceCatalog
{
    public bool TryGetBySourceKey(string sourceKey, out CollectionSourceEntry? entry) =>
        CollectionSourceCatalog.TryGetBySourceKey(sourceKey, out entry);
}
