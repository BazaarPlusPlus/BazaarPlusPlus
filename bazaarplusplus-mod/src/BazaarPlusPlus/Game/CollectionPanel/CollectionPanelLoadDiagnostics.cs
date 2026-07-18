#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure;

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
            CollectionPanelLogEvents.LoadCompleted,
            () =>
                [
                    CollectionPanelLogEvents.LoadPhase.Bind(phase),
                    CollectionPanelLogEvents.LoadOutcome.Bind(outcome),
                    CollectionPanelLogEvents.LoadReasonCode.Bind(reasonCode),
                    CollectionPanelLogEvents.LoadDurationMs.Bind(
                        ElapsedMs(_startedAt, _timestampProvider())
                    ),
                    CollectionPanelLogEvents.LoadCatalogAcquireDurationMs.Bind(
                        _catalogAcquireDurationMs
                    ),
                    CollectionPanelLogEvents.LoadCatalogDurationMs.Bind(_catalogDurationMs),
                    CollectionPanelLogEvents.LoadFilterDurationMs.Bind(_filterDurationMs),
                    CollectionPanelLogEvents.LoadRefreshDurationMs.Bind(_refreshDurationMs),
                    CollectionPanelLogEvents.LoadCatalogCacheHit.Bind(_catalogCacheHit),
                    CollectionPanelLogEvents.LoadSourceTemplateCount.Bind(_sourceTemplateCount),
                    CollectionPanelLogEvents.LoadAcceptedCount.Bind(_acceptedCount),
                    CollectionPanelLogEvents.LoadRejectedCount.Bind(_rejectedCount),
                    CollectionPanelLogEvents.LoadCatalogCardCount.Bind(_catalogCardCount),
                    CollectionPanelLogEvents.LoadVisibleCardCount.Bind(_visibleCardCount),
                ]
        );
    }

    private static double ElapsedMs(long start, long end) =>
        (end - start) * 1000.0 / Stopwatch.Frequency;
}
