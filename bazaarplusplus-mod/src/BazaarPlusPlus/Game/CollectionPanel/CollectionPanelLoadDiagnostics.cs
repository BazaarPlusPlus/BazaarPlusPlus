#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPanelLoadDiagnostics
{
    private readonly Func<long> _timestampProvider;
    private readonly long _startedAt;
    private double? _catalogAcquireDurationMs;
    private double? _catalogDurationMs;
    private double? _filterDurationMs;
    private double? _refreshDurationMs;
    private bool? _catalogCacheHit;
    private int? _sourceTemplateCount;
    private int? _acceptedCount;
    private int? _rejectedCount;
    private int? _catalogCardCount;
    private int? _visibleCardCount;

    internal CollectionPanelLoadDiagnostics()
        : this(Stopwatch.GetTimestamp) { }

    internal CollectionPanelLoadDiagnostics(Func<long> timestampProvider)
    {
        _timestampProvider =
            timestampProvider ?? throw new ArgumentNullException(nameof(timestampProvider));
#if DEBUG
        _startedAt = _timestampProvider();
#else
        _startedAt = 0L;
#endif
    }

    public long Now()
    {
#if DEBUG
        return _timestampProvider();
#else
        return 0L;
#endif
    }

    [Conditional("DEBUG")]
    internal void AddSegment(CollectionPanelLoadSegment segment, long startedAt)
    {
        var elapsed = ElapsedMs(startedAt, _timestampProvider());
        switch (segment)
        {
            case CollectionPanelLoadSegment.CatalogAcquire:
                _catalogAcquireDurationMs = elapsed;
                break;
            case CollectionPanelLoadSegment.Catalog:
                _catalogDurationMs = elapsed;
                break;
            case CollectionPanelLoadSegment.Filter:
                _filterDurationMs = elapsed;
                break;
            case CollectionPanelLoadSegment.Refresh:
                _refreshDurationMs = elapsed;
                break;
        }
    }

    [Conditional("DEBUG")]
    internal void SetCatalogResult(
        bool cacheHit,
        int sourceTemplateCount,
        int acceptedCount,
        int rejectedCount
    )
    {
        _catalogCacheHit = cacheHit;
        _sourceTemplateCount = sourceTemplateCount;
        _acceptedCount = acceptedCount;
        _rejectedCount = rejectedCount;
    }

    [Conditional("DEBUG")]
    internal void SetFinalCounts(int catalogCardCount, int visibleCardCount)
    {
        _catalogCardCount = catalogCardCount;
        _visibleCardCount = visibleCardCount;
    }

    [Conditional("DEBUG")]
    internal void Complete(
        CollectionPanelLoadPhase phase,
        CollectionPanelLoadOutcome outcome,
        CollectionPanelLogReasonCode? reasonCode
    )
    {
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.CollectionPanel, "collection_panel.load.completed"),
            () =>
                [
                    ("phase", phase),
                    ("outcome", outcome),
                    ("reason_code", reasonCode),
                    ("duration_ms", ElapsedMs(_startedAt, _timestampProvider())),
                    ("catalog_acquire_duration_ms", _catalogAcquireDurationMs),
                    ("catalog_duration_ms", _catalogDurationMs),
                    ("filter_duration_ms", _filterDurationMs),
                    ("refresh_duration_ms", _refreshDurationMs),
                    ("catalog_cache_hit", _catalogCacheHit),
                    ("source_template_count", _sourceTemplateCount),
                    ("accepted_count", _acceptedCount),
                    ("rejected_count", _rejectedCount),
                    ("catalog_card_count", _catalogCardCount),
                    ("visible_card_count", _visibleCardCount),
                ]
        );
    }

    private static double ElapsedMs(long start, long end) =>
        (end - start) * 1000.0 / Stopwatch.Frequency;
}
