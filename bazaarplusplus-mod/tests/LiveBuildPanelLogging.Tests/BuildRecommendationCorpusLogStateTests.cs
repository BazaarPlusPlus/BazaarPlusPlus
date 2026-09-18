#nullable enable
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace LiveBuildPanelLogging.Tests;

public sealed class BuildRecommendationCorpusLogStateTests : IDisposable
{
    public BuildRecommendationCorpusLogStateTests() => BppLog.Reset();

    public void Dispose() => BppLog.Reset();

    [Fact]
    public void Fresh_initial_corpus_emits_ready_once()
    {
        var state = new BuildRecommendationCorpusLogState();

        state.ReportReady(LiveBuildCorpusSource.Cache, 27);
        state.ReportReady(LiveBuildCorpusSource.Cache, 27);

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal("Info", captured.Severity);
        Assert.Equal("live_build_panel.corpus.ready", captured.Definition.EventId);
    }

    [Fact]
    public void Distinct_degradation_reasons_emit_once_and_background_success_recovers_once()
    {
        var state = new BuildRecommendationCorpusLogState();
        var stale = new CorpusDegradation(
            LiveBuildCorpusReasonCode.StaleCache,
            LiveBuildCorpusSource.Cache,
            12,
            Expired: true,
            "/Users/example/builds.json",
            Exception: null
        );
        var queueFailed = new CorpusDegradation(
            LiveBuildCorpusReasonCode.RefreshQueueFailed,
            LiveBuildCorpusSource.Cache,
            12,
            Expired: false,
            CachePath: null,
            Exception: null
        );

        state.ReportDegraded(stale);
        state.ReportDegraded(stale);
        state.ReportDegraded(queueFailed);
        state.ReportDegraded(queueFailed);
        state.ReportRecovered(LiveBuildCorpusSource.Remote, 15);
        state.ReportRecovered(LiveBuildCorpusSource.Remote, 15);

        Assert.Equal(
            [
                "live_build_panel.corpus.degraded",
                "live_build_panel.corpus.degraded",
                "live_build_panel.corpus.recovered",
            ],
            BppLog.Events.Select(item => item.Definition.EventId)
        );
        Assert.Equal(2, BppLog.StormRecoveries.Count);
    }

    [Fact]
    public void Manual_success_resets_degradation_and_storms_without_a_recovery_Info()
    {
        var state = new BuildRecommendationCorpusLogState();
        state.ReportDegraded(
            new CorpusDegradation(
                LiveBuildCorpusReasonCode.StaleCache,
                LiveBuildCorpusSource.Cache,
                12,
                Expired: true,
                "/tmp/builds.json",
                Exception: null
            )
        );
        BppLog.Reset();

        state.ResetDegradedSilently();
        state.ReportRecovered(LiveBuildCorpusSource.Remote, 20);

        Assert.Empty(BppLog.Events);
        Assert.Single(BppLog.StormRecoveries);
    }

    [Fact]
    public void Embedded_invalid_is_reported_as_the_single_initial_warning()
    {
        var state = new BuildRecommendationCorpusLogState();
        state.ReportDegraded(
            new CorpusDegradation(
                LiveBuildCorpusReasonCode.EmbeddedInvalid,
                LiveBuildCorpusSource.Embedded,
                BuildCount: 0,
                Expired: false,
                CachePath: null,
                Exception: null
            )
        );

        var warning = Assert.Single(BppLog.Events);
        Assert.Equal("Warning", warning.Severity);
        Assert.Equal(LiveBuildCorpusReasonCode.EmbeddedInvalid, Value(warning, "reason_code"));
    }

    [Fact]
    public void Cache_write_warning_is_independent_and_resets_silently_after_success()
    {
        var state = new BuildRecommendationCorpusLogState();
        var first = new IOException("write failed");

        state.ReportCacheWriteDegraded("/tmp/builds.json", first);
        state.ReportCacheWriteDegraded("/tmp/builds.json", first);
        state.ReportCacheWriteRecovered();
        state.ReportCacheWriteDegraded("/tmp/builds.json", first);

        Assert.Equal(2, BppLog.Events.Count);
        Assert.All(
            BppLog.Events,
            item =>
                Assert.Equal(
                    "live_build_panel.corpus.cache_write_degraded",
                    item.Definition.EventId
                )
        );
        Assert.Single(BppLog.StormRecoveries);
    }

    [Fact]
    public void Debug_observations_are_compile_time_excluded_from_Release()
    {
        var state = new BuildRecommendationCorpusLogState();
        state.ReportCacheLoaded(10, expired: false, "/tmp/builds.json");
        state.ReportRemoteLoaded(11);

#if DEBUG
        Assert.Equal(2, BppLog.Events.Count);
#else
        Assert.Empty(BppLog.Events);
#endif
    }

    private static object? Value(CapturedBppLogEvent captured, string fieldName) =>
        captured.Values.Single(value => value.Field.Name == fieldName).Value;
}
