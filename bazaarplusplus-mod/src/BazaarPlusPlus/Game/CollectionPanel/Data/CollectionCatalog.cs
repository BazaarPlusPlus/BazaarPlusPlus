#nullable enable
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionCatalog
{
    private readonly BppStaticCardMapProvider _cardMapProvider;
    private IReadOnlyList<CollectionCardVm>? _cache;
    private object? _cacheSource;
    private int _cacheSourceTemplateCount;
    private readonly CollectionCatalogLogState _logState = new();

    public CollectionCatalog(BppStaticCardMapProvider cardMapProvider)
    {
        _cardMapProvider =
            cardMapProvider ?? throw new ArgumentNullException(nameof(cardMapProvider));
    }

    public bool TryGetCached(out CollectionCatalogBuildResult result)
    {
        result = EmptyResult(wasCacheHit: false);
        var source = BppStaticDataAccess.TryGetReadyManagerObject();
        if (source == null || _cache == null)
            return false;

        if (!ReferenceEquals(source, _cacheSource))
        {
            InvalidateCache(CollectionPanelLogReasonCode.StaticDataManagerChanged);
            return false;
        }

        result = new CollectionCatalogBuildResult(
            _cache,
            _cacheSourceTemplateCount,
            _cacheSourceTemplateCount,
            _cache.Count,
            Math.Max(0, _cacheSourceTemplateCount - _cache.Count),
            wasCacheHit: true
        );
        return true;
    }

    /// <summary>
    /// True once an off-thread card-map load has been kicked for the current static-data source.
    /// </summary>
    public bool HasCardMapLoadStarted => _cardMapProvider.HasLoadStartedForCurrentSource;

    /// <summary>
    /// Kicks (or returns the in-flight) off-thread load of the full game card map so the heavy
    /// <c>ReadAllCards</c> SQLite read never runs on the Unity main thread. Idempotent per
    /// static-data manager: repeated calls for the same source share one Task; a changed source
    /// (runtime swap) re-kicks. Returns <c>null</c> only when static data is not ready yet
    /// (non-blocking). <paramref name="source"/> is the manager the Task loads from.
    /// </summary>
    public Task<Dictionary<Guid, ITCard>?>? BeginCardMapLoad(out object? source)
    {
        return _cardMapProvider.BeginLoad(out source);
    }

    /// <summary>
    /// Builds a catalog session from a card map already materialised by
    /// <see cref="BeginCardMapLoad"/> (kept off the main thread). The session then enumerates the
    /// map on the time-sliced build loop. <paramref name="outcome"/> retains any off-thread
    /// failure so this catalog remains the sole owner of the unavailable-state transition.
    /// </summary>
    public bool TryCreateBuildSession(
        CollectionCardMapLoadOutcome outcome,
        out CollectionCatalogBuildSession? session,
        out CollectionPanelLogReasonCode? unavailableReason
    )
    {
        session = null;
        unavailableReason = outcome.FailureReason;

        if (outcome.Source == null)
        {
            unavailableReason = CollectionPanelLogReasonCode.StaticDataNotReady;
            BppLog.DebugEvent(
                CollectionPanelLogEvents.CatalogBuildDeferred,
                static () =>
                    [
                        CollectionPanelLogEvents.CatalogBuildDeferredReasonCode.Bind(
                            CollectionPanelLogReasonCode.StaticDataNotReady
                        ),
                    ]
            );
            return false;
        }

        if (!outcome.IsAvailable || outcome.Map == null)
        {
            var reason = outcome.FailureReason ?? CollectionPanelLogReasonCode.CardMapNull;
            unavailableReason = reason;
            _logState.ReportDegraded(reason, outcome.Exception);
            return false;
        }

        session = new CollectionCatalogBuildSession(outcome.Source, outcome.Map);
        return true;
    }

    public CollectionCatalogBuildResult Commit(CollectionCatalogBuildSession session)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (!session.IsComplete)
            throw new InvalidOperationException("Catalog build session is not complete.");

        _cache = session.Cards;
        _cacheSource = session.Source;
        _cacheSourceTemplateCount = session.SourceTemplateCount;

        var result = new CollectionCatalogBuildResult(
            session.Cards,
            session.SourceTemplateCount,
            session.ScannedCount,
            session.AcceptedCount,
            session.RejectedCount,
            wasCacheHit: false
        );
        _logState.ReportBuilt(
            result.AcceptedCount,
            result.RejectedCount,
            result.SourceTemplateCount
        );
        return result;
    }

    public void InvalidateCache(CollectionPanelLogReasonCode reasonCode)
    {
        if (_cache != null)
            _logState.ReportInvalidated(reasonCode);
        _cache = null;
        _cacheSource = null;
        _cacheSourceTemplateCount = 0;
    }

    private static CollectionCatalogBuildResult EmptyResult(bool wasCacheHit) =>
        new(
            Array.Empty<CollectionCardVm>(),
            sourceTemplateCount: 0,
            scannedCount: 0,
            acceptedCount: 0,
            rejectedCount: 0,
            wasCacheHit: wasCacheHit
        );
}
