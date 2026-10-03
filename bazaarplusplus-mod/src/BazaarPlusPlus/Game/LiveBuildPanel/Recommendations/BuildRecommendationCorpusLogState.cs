#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

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
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.LiveBuildPanel,
                "live_build_panel.corpus.warmup_started"
            ),
            static () => []
        );

    internal void ReportReady(LiveBuildCorpusSource source, int buildCount)
    {
        lock (_sync)
        {
            if (_health != CorpusHealth.Waiting)
                return;
            _health = CorpusHealth.Healthy;
        }

        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.LiveBuildPanel, "live_build_panel.corpus.ready"),
            ("source", source),
            ("build_count", buildCount)
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

        var fields = new BppLogField[]
        {
            ("reason_code", degradation.ReasonCode),
            ("source", degradation.Source),
            ("build_count", degradation.BuildCount),
            ("expired", degradation.Expired),
            ("cache_path", degradation.CachePath),
        };
        if (degradation.Exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.LiveBuildPanel,
                    "live_build_panel.corpus.degraded",
                    storm: ["reason_code"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.LiveBuildPanel,
                    "live_build_panel.corpus.degraded",
                    storm: ["reason_code"]
                ),
                degradation.Exception,
                fields
            );
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
            new BppLogEvent(BppLogFeatureScope.LiveBuildPanel, "live_build_panel.corpus.recovered"),
            ("source", source),
            ("build_count", buildCount)
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
            new BppLogEvent(
                BppLogFeatureScope.LiveBuildPanel,
                "live_build_panel.corpus.refresh_queued"
            ),
            () => [("reason_code", reasonCode)]
        );

    internal void ReportCacheLoaded(int buildCount, bool expired, string cachePath) =>
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.LiveBuildPanel,
                "live_build_panel.corpus.cache_loaded"
            ),
            () => [("build_count", buildCount), ("expired", expired), ("cache_path", cachePath)]
        );

    internal void ReportRemoteLoaded(int buildCount) =>
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.LiveBuildPanel,
                "live_build_panel.corpus.remote_loaded"
            ),
            () => [("endpoint", LiveBuildCorpusEndpoint.TenWinBuilds), ("build_count", buildCount)]
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
            new BppLogEvent(
                BppLogFeatureScope.LiveBuildPanel,
                "live_build_panel.corpus.cache_write_degraded",
                storm: ["reason_code"]
            ),
            exception,
            ("path", path),
            ("reason_code", LiveBuildCacheWriteReasonCode.WriteFailed)
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
            new BppLogEvent(
                BppLogFeatureScope.LiveBuildPanel,
                "live_build_panel.corpus.cache_write_degraded",
                storm: ["reason_code"]
            ),
            ("reason_code", LiveBuildCacheWriteReasonCode.WriteFailed)
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
                new BppLogEvent(
                    BppLogFeatureScope.LiveBuildPanel,
                    "live_build_panel.corpus.degraded",
                    storm: ["reason_code"]
                ),
                ("reason_code", reason)
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
