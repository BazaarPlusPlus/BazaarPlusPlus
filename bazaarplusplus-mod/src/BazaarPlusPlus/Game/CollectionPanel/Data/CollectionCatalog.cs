#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CollectionPanel.Data;

internal sealed class CollectionCatalog
{
    private IReadOnlyList<CollectionCardVm>? _cache;
    private object? _cacheSource;
    private int _cacheSourceTemplateCount;
    private Task<Dictionary<Guid, ITCard>?>? _cardMapTask;
    private object? _cardMapTaskSource;

    public bool TryGetCached(out CollectionCatalogBuildResult result)
    {
        result = EmptyResult(wasCacheHit: false);
        var source = BppStaticDataAccess.TryGetReadyManagerObject();
        if (source == null || _cache == null)
            return false;

        if (!ReferenceEquals(source, _cacheSource))
        {
            InvalidateCache("static-data-manager-changed");
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
        BppLog.Info(
            "CollectionCatalog",
            $"Catalog cache hit: {result.AcceptedCount} cards from {result.SourceTemplateCount} templates."
        );
        return true;
    }

    /// <summary>
    /// True once an off-thread card-map load has been kicked for the current static-data source.
    /// </summary>
    public bool HasCardMapLoadStarted => _cardMapTask != null;

    /// <summary>
    /// Kicks (or returns the in-flight) off-thread load of the full game card map so the heavy
    /// <c>ReadAllCards</c> SQLite read never runs on the Unity main thread. Idempotent per
    /// static-data manager: repeated calls for the same source share one Task; a changed source
    /// (runtime swap) re-kicks. Returns <c>null</c> only when static data is not ready yet
    /// (non-blocking). <paramref name="source"/> is the manager the Task loads from.
    /// </summary>
    public Task<Dictionary<Guid, ITCard>?>? BeginCardMapLoad(out object? source)
    {
        source = BppStaticDataAccess.TryGetReadyManagerObject();
        if (source == null)
            return null;

        if (_cardMapTask != null && ReferenceEquals(_cardMapTaskSource, source))
            return _cardMapTask;

        var captured = source;
        _cardMapTaskSource = source;
        _cardMapTask = Task.Run(() => BppStaticDataAccess.LoadCardMap(captured));
        return _cardMapTask;
    }

    /// <summary>
    /// Builds a catalog session from a card map already materialised by
    /// <see cref="BeginCardMapLoad"/> (kept off the main thread). The session then enumerates the
    /// map on the time-sliced build loop. Exceptions from the off-thread load are surfaced by the
    /// caller via the Task; a null <paramref name="map"/> yields an unavailable reason here.
    /// </summary>
    public bool TryCreateBuildSession(
        object? source,
        Dictionary<Guid, ITCard>? map,
        out CollectionCatalogBuildSession? session,
        out string unavailableReason
    )
    {
        session = null;
        unavailableReason = string.Empty;

        if (source == null)
        {
            unavailableReason = "static-data-not-ready";
            BppLog.Debug(
                "CollectionCatalog",
                "Static data manager not yet ready; catalog build deferred."
            );
            return false;
        }

        if (map == null)
        {
            unavailableReason = "card-map-null";
            BppLog.Warn("CollectionCatalog", "GetCardMap() returned null.");
            return false;
        }

        session = new CollectionCatalogBuildSession(source, map);
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
        BppLog.Info(
            "CollectionCatalog",
            $"Catalog built: {result.AcceptedCount} cards from {result.SourceTemplateCount} templates, rejected={result.RejectedCount}."
        );
        return result;
    }

    public void InvalidateCache(string reason)
    {
        if (_cache != null)
            BppLog.Info("CollectionCatalog", $"Catalog cache invalidated: reason={reason}.");
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
