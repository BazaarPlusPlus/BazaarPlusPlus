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
    private CollectionCatalogBuildSession? _buildSession;
    private object? _buildSource;
    private object? _unavailableSource;
    private CollectionPanelLogReasonCode? _unavailableReason;
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

    public CollectionCatalogWarmupStatus WarmupStatus { get; private set; } =
        CollectionCatalogWarmupStatus.WaitingForStaticData;

    public CollectionPanelLogReasonCode? WarmupFailureReason => _unavailableReason;

    /// <summary>
    /// Advances the shared catalog build by at most <paramref name="frameBudgetMs"/> on the
    /// Unity main thread. The card-map acquisition remains in the provider's worker task; only
    /// the projection of game templates into collection VMs is time-sliced here. The closed-panel
    /// warmup tick and the open-panel loading path observe the same session, which can only be
    /// committed once.
    /// </summary>
    public CollectionCatalogWarmupStatus AdvanceBuild(
        float frameBudgetMs,
        out CollectionCatalogBuildResult? completed
    )
    {
        completed = null;
        if (TryGetCached(out _))
        {
            WarmupStatus = CollectionCatalogWarmupStatus.Ready;
            return WarmupStatus;
        }

        var loadTask = BeginCardMapLoad(out var source);
        if (source == null || loadTask == null)
        {
            WarmupStatus = CollectionCatalogWarmupStatus.WaitingForStaticData;
            return WarmupStatus;
        }

        if (_unavailableSource != null && !ReferenceEquals(source, _unavailableSource))
            ClearBuildState();
        if (_buildSession != null && !ReferenceEquals(source, _buildSource))
            ClearBuildState();

        if (_unavailableSource != null && ReferenceEquals(source, _unavailableSource))
        {
            WarmupStatus = CollectionCatalogWarmupStatus.Unavailable;
            return WarmupStatus;
        }

        if (!loadTask.IsCompleted)
        {
            WarmupStatus = CollectionCatalogWarmupStatus.LoadingCardMap;
            return WarmupStatus;
        }

        if (_buildSession == null)
        {
            var outcome = CollectionCardMapLoadOutcome.From(source, loadTask);
            if (!TryCreateBuildSession(outcome, out var session, out var unavailableReason))
            {
                _unavailableSource = source;
                _unavailableReason = unavailableReason;
                WarmupStatus = CollectionCatalogWarmupStatus.Unavailable;
                return WarmupStatus;
            }

            _buildSession = session!;
            _buildSource = source;
        }

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var sessionCompleted = _buildSession.Step(
            () =>
            {
                var elapsedMs =
                    (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt)
                    * 1000.0
                    / System.Diagnostics.Stopwatch.Frequency;
                return elapsedMs >= Math.Max(0f, frameBudgetMs);
            },
            minimumTemplates: frameBudgetMs <= 1f ? 8 : 32
        );
        if (!sessionCompleted)
        {
            WarmupStatus = CollectionCatalogWarmupStatus.Building;
            return WarmupStatus;
        }

        var buildSession = _buildSession;
        _buildSession = null;
        _buildSource = null;
        _unavailableSource = null;
        _unavailableReason = null;
        try
        {
            completed = Commit(buildSession);
            WarmupStatus = CollectionCatalogWarmupStatus.Ready;
        }
        finally
        {
            buildSession.Dispose();
        }
        return WarmupStatus;
    }

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
        ClearBuildState();
        WarmupStatus = CollectionCatalogWarmupStatus.WaitingForStaticData;
    }

    private void ClearBuildState()
    {
        _buildSession?.Dispose();
        _buildSession = null;
        _buildSource = null;
        _unavailableSource = null;
        _unavailableReason = null;
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
