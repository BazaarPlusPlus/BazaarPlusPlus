#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

// A built source catalog with its lookups; each host loads the catalog JSON and wraps the
// entries here.
public sealed class CollectionSourceIndex : ICollectionSourceCatalog
{
    private readonly IReadOnlyDictionary<Guid, CollectionSourceEntry> _bySourceTemplateId;
    private readonly IReadOnlyDictionary<string, CollectionSourceEntry> _bySourceKey;

    public CollectionSourceIndex(IReadOnlyList<CollectionSourceEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        var byTemplate = new Dictionary<Guid, CollectionSourceEntry>();
        var byKey = new Dictionary<string, CollectionSourceEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var id in entry.SourceTemplateIds)
                byTemplate[id] = entry;
            byKey[entry.SourceKey] = entry;
        }
        _bySourceTemplateId = byTemplate;
        _bySourceKey = byKey;
    }

    public static CollectionSourceIndex Empty { get; } = new(Array.Empty<CollectionSourceEntry>());

    public IReadOnlyList<CollectionSourceEntry> Entries { get; }

    public int SourceTemplateCount => _bySourceTemplateId.Count;

    public bool TryGet(Guid sourceTemplateId, out CollectionSourceEntry? entry) =>
        _bySourceTemplateId.TryGetValue(sourceTemplateId, out entry);

    public bool TryGetBySourceKey(string sourceKey, out CollectionSourceEntry? entry) =>
        _bySourceKey.TryGetValue(sourceKey, out entry);

    public IEnumerable<CollectionSourceEntry> For(
        CollectionSourceKind kind,
        CardHero effectiveHero
    ) => CollectionSourceCatalog.VisibleEntries(Entries, kind, effectiveHero);
}
