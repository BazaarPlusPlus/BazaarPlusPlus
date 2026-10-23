#nullable enable
using System.Reflection;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

// The desktop host's source catalog: the embedded collection-sources.json, parsed once by the
// Core CollectionSourceCatalog against this game build's heroes.
internal sealed class StaticCollectionSourceCatalog : ICollectionSourceCatalog
{
    private const string ResourceSuffix = "collection-sources.json";

    private static readonly object SyncRoot = new();
    private static CollectionSourceIndex? _index;

    public static IReadOnlyList<CollectionSourceEntry> Entries => Index.Entries;

    public bool TryGetBySourceKey(string sourceKey, out CollectionSourceEntry? entry) =>
        Index.TryGetBySourceKey(sourceKey, out entry);

    public IEnumerable<CollectionSourceEntry> For(
        CollectionSourceKind kind,
        CardHero effectiveHero
    ) => Index.For(kind, effectiveHero);

    private static CollectionSourceIndex Index
    {
        get
        {
            var index = _index;
            if (index != null)
                return index;

            lock (SyncRoot)
            {
                return _index ??= Load();
            }
        }
    }

    private static CollectionSourceIndex Load()
    {
        var load = LoadEmbeddedCatalog(ResourceSuffix);
        if (load.Entries != null)
        {
            var index = new CollectionSourceIndex(load.Entries);
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

        var fields = new BppLogField[]
        {
            ("reason_code", load.ReasonCode),
            ("resource_suffix", ResourceSuffix),
        };
        var logEvent = new BppLogEvent(
            BppLogFeatureScope.CollectionPanel,
            "collection_panel.source_catalog.load_failed"
        );
        if (load.Exception == null)
            BppLog.ErrorEvent(logEvent, fields);
        else
            BppLog.ErrorEvent(logEvent, load.Exception, fields);
        return CollectionSourceIndex.Empty;
    }

    private static SourceCatalogLoadOutcome LoadEmbeddedCatalog(string suffix)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                return SourceCatalogLoadOutcome.Failed(
                    CollectionPanelLogReasonCode.ResourceMissing,
                    null
                );
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return SourceCatalogLoadOutcome.Failed(
                    CollectionPanelLogReasonCode.ResourceStreamUnavailable,
                    null
                );
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return SourceCatalogLoadOutcome.Loaded(
                CollectionSourceCatalog.Build(json, DesktopCardFacets.IsHeroAvailable)
            );
        }
        catch (Exception ex)
        {
            return SourceCatalogLoadOutcome.Failed(
                CollectionPanelLogReasonCode.SourceCatalogInvalid,
                ex
            );
        }
    }

    private readonly record struct SourceCatalogLoadOutcome(
        IReadOnlyList<CollectionSourceEntry>? Entries,
        CollectionPanelLogReasonCode? ReasonCode,
        Exception? Exception
    )
    {
        internal static SourceCatalogLoadOutcome Loaded(
            IReadOnlyList<CollectionSourceEntry> entries
        ) => new(entries, null, null);

        internal static SourceCatalogLoadOutcome Failed(
            CollectionPanelLogReasonCode reasonCode,
            Exception? exception
        ) => new(null, reasonCode, exception);
    }
}
