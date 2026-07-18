#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal readonly record struct CorpusDegradation(
    LiveBuildCorpusReasonCode ReasonCode,
    LiveBuildCorpusSource Source,
    int BuildCount,
    bool Expired,
    string? CachePath,
    Exception? Exception
);

internal sealed class BuildRecommendationCorpusLogState
{
    private readonly object _sync = new();
    private readonly HashSet<LiveBuildCorpusReasonCode> _degradationReasons = [];
    private CorpusHealth _health = CorpusHealth.Waiting;
    private bool _cacheWriteDegraded;

    internal void ReportWarmupStarted() =>
        BppLog.DebugEvent(LiveBuildPanelLogEvents.CorpusWarmupStarted, static () => []);

    internal void ReportReady(LiveBuildCorpusSource source, int buildCount)
    {
        lock (_sync)
        {
            if (_health != CorpusHealth.Waiting)
                return;
            _health = CorpusHealth.Healthy;
        }

        BppLog.InfoEvent(
            LiveBuildPanelLogEvents.CorpusReady,
            LiveBuildPanelLogEvents.CorpusReadySource.Bind(source),
            LiveBuildPanelLogEvents.CorpusReadyBuildCount.Bind(buildCount)
        );
    }

    internal void ReportDegraded(CorpusDegradation degradation)
    {
        lock (_sync)
        {
            _health = CorpusHealth.Degraded;
            if (!_degradationReasons.Add(degradation.ReasonCode))
                return;
        }

        var fields = new[]
        {
            LiveBuildPanelLogEvents.CorpusDegradedReasonCode.Bind(degradation.ReasonCode),
            LiveBuildPanelLogEvents.CorpusDegradedSource.Bind(degradation.Source),
            LiveBuildPanelLogEvents.CorpusDegradedBuildCount.Bind(degradation.BuildCount),
            LiveBuildPanelLogEvents.CorpusDegradedExpired.Bind(degradation.Expired),
            LiveBuildPanelLogEvents.CorpusDegradedCachePath.Bind(degradation.CachePath),
        };
        if (degradation.Exception == null)
            BppLog.WarnEvent(LiveBuildPanelLogEvents.CorpusDegraded, fields);
        else
            BppLog.WarnEvent(LiveBuildPanelLogEvents.CorpusDegraded, degradation.Exception, fields);
    }

    internal void ReportRecovered(LiveBuildCorpusSource source, int buildCount)
    {
        LiveBuildCorpusReasonCode[] reasons;
        lock (_sync)
        {
            if (_health != CorpusHealth.Degraded)
                return;
            _health = CorpusHealth.Healthy;
            reasons = SnapshotAndClearReasons();
        }

        RecoverCorpusStorms(reasons);
        BppLog.InfoEvent(
            LiveBuildPanelLogEvents.CorpusRecovered,
            LiveBuildPanelLogEvents.CorpusRecoveredSource.Bind(source),
            LiveBuildPanelLogEvents.CorpusRecoveredBuildCount.Bind(buildCount)
        );
    }

    internal void ResetDegradedSilently()
    {
        LiveBuildCorpusReasonCode[] reasons;
        lock (_sync)
        {
            _health = CorpusHealth.Healthy;
            reasons = SnapshotAndClearReasons();
        }
        RecoverCorpusStorms(reasons);
    }

    internal void ReportRefreshQueued(LiveBuildCorpusReasonCode reasonCode) =>
        BppLog.DebugEvent(
            LiveBuildPanelLogEvents.CorpusRefreshQueued,
            () => [LiveBuildPanelLogEvents.CorpusRefreshQueuedReasonCode.Bind(reasonCode)]
        );

    internal void ReportCacheLoaded(int buildCount, bool expired, string cachePath) =>
        BppLog.DebugEvent(
            LiveBuildPanelLogEvents.CorpusCacheLoaded,
            () =>
                [
                    LiveBuildPanelLogEvents.CorpusCacheLoadedBuildCount.Bind(buildCount),
                    LiveBuildPanelLogEvents.CorpusCacheLoadedExpired.Bind(expired),
                    LiveBuildPanelLogEvents.CorpusCacheLoadedCachePath.Bind(cachePath),
                ]
        );

    internal void ReportRemoteLoaded(int buildCount) =>
        BppLog.DebugEvent(
            LiveBuildPanelLogEvents.CorpusRemoteLoaded,
            () =>
                [
                    LiveBuildPanelLogEvents.CorpusRemoteLoadedEndpoint.Bind(
                        LiveBuildCorpusEndpoint.TenWinBuilds
                    ),
                    LiveBuildPanelLogEvents.CorpusRemoteLoadedBuildCount.Bind(buildCount),
                ]
        );

    internal void ReportCacheWriteDegraded(string? path, Exception exception)
    {
        lock (_sync)
        {
            if (_cacheWriteDegraded)
                return;
            _cacheWriteDegraded = true;
        }

        BppLog.WarnEvent(
            LiveBuildPanelLogEvents.CacheWriteDegraded,
            exception,
            LiveBuildPanelLogEvents.CacheWriteDegradedPath.Bind(path),
            LiveBuildPanelLogEvents.CacheWriteDegradedReasonCode.Bind(
                LiveBuildCacheWriteReasonCode.WriteFailed
            )
        );
    }

    internal void ReportCacheWriteRecovered()
    {
        lock (_sync)
        {
            if (!_cacheWriteDegraded)
                return;
            _cacheWriteDegraded = false;
        }

        BppLog.RecoverStorm(
            LiveBuildPanelLogEvents.CacheWriteDegraded,
            LiveBuildPanelLogEvents.CacheWriteDegradedReasonCode.Bind(
                LiveBuildCacheWriteReasonCode.WriteFailed
            )
        );
    }

    private LiveBuildCorpusReasonCode[] SnapshotAndClearReasons()
    {
        var reasons = new LiveBuildCorpusReasonCode[_degradationReasons.Count];
        _degradationReasons.CopyTo(reasons);
        _degradationReasons.Clear();
        return reasons;
    }

    private static void RecoverCorpusStorms(IEnumerable<LiveBuildCorpusReasonCode> reasons)
    {
        foreach (var reason in reasons)
        {
            BppLog.RecoverStorm(
                LiveBuildPanelLogEvents.CorpusDegraded,
                LiveBuildPanelLogEvents.CorpusDegradedReasonCode.Bind(reason)
            );
        }
    }

    private enum CorpusHealth
    {
        Waiting,
        Healthy,
        Degraded,
    }
}
