#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

// The merchant and trainer source catalog every host filters with: Core's embedded
// collection-sources.json, parsed once against the heroes the host's game build has. A load
// failure logs and leaves the catalog empty, so the source filter offers nothing instead of
// failing the panel.
public sealed class EmbeddedCollectionSourceCatalog : ICollectionSourceCatalog
{
    private const string ResourceSuffix = "collection-sources.json";

    private readonly Func<CardHero, bool> _isHeroAvailable;
    private readonly object _syncRoot = new();
    private CollectionSourceIndex? _index;

    public EmbeddedCollectionSourceCatalog(Func<CardHero, bool> isHeroAvailable) =>
        _isHeroAvailable =
            isHeroAvailable ?? throw new ArgumentNullException(nameof(isHeroAvailable));

    public IReadOnlyList<CollectionSourceEntry> Entries => Index.Entries;

    public bool TryGetBySourceKey(string sourceKey, out CollectionSourceEntry? entry) =>
        Index.TryGetBySourceKey(sourceKey, out entry);

    public IEnumerable<CollectionSourceEntry> For(
        CollectionSourceKind kind,
        CardHero effectiveHero
    ) => Index.For(kind, effectiveHero);

    private CollectionSourceIndex Index
    {
        get
        {
            var index = _index;
            if (index != null)
                return index;

            lock (_syncRoot)
            {
                return _index ??= Load();
            }
        }
    }

    private CollectionSourceIndex Load()
    {
        string reasonCode;
        Exception? failure = null;
        try
        {
            var assembly = typeof(EmbeddedCollectionSourceCatalog).Assembly;
            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name =>
                    name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase)
                );
            using var stream =
                resourceName == null ? null : assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var index = new CollectionSourceIndex(
                    CollectionSourceCatalog.Build(reader.ReadToEnd(), _isHeroAvailable)
                );
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CollectionPanel,
                        "collection_panel.source_catalog.loaded"
                    ),
                    () =>
                        [
                            ("entry_count", index.Entries.Count),
                            ("source_template_count", index.SourceTemplateCount),
                        ]
                );
                return index;
            }
            reasonCode = resourceName == null ? "resource_missing" : "resource_stream_unavailable";
        }
        catch (Exception exception)
        {
            reasonCode = "source_catalog_invalid";
            failure = exception;
        }

        var logEvent = new BppLogEvent(
            BppLogFeatureScope.CollectionPanel,
            "collection_panel.source_catalog.load_failed"
        );
        BppLogField[] fields = [("reason_code", reasonCode), ("resource_suffix", ResourceSuffix)];
        if (failure == null)
            BppLog.ErrorEvent(logEvent, fields);
        else
            BppLog.ErrorEvent(logEvent, failure, fields);
        return CollectionSourceIndex.Empty;
    }
}
