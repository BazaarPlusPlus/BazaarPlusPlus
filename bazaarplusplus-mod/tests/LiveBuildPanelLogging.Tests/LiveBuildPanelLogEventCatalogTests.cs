#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace LiveBuildPanelLogging.Tests;

public sealed class LiveBuildPanelLogEventCatalogTests
{
    [Fact]
    public void Events_match_the_locked_D44_to_D58_schemas()
    {
        var actual = Definitions()
            .ToDictionary(definition => definition.EventId, DescribeFields, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["live_build_panel.mount.failed"] = "reason_code:Low:None",
            ["live_build_panel.refresh.succeeded"] = "request_id:High:Short|result:Low:None",
            ["live_build_panel.refresh.failed"] = "request_id:High:Short|reason_code:Low:None",
            ["live_build_panel.corpus.warmup_started"] = "",
            ["live_build_panel.corpus.degraded"] =
                "reason_code:Low:None|source:Low:None|build_count:High:None|expired:Low:None|cache_path:High:None",
            ["live_build_panel.corpus.ready"] = "source:Low:None|build_count:High:None",
            ["live_build_panel.corpus.refresh_queued"] = "reason_code:Low:None",
            ["live_build_panel.corpus.recovered"] = "source:Low:None|build_count:High:None",
            ["live_build_panel.corpus.cache_loaded"] =
                "build_count:High:None|expired:Low:None|cache_path:High:None",
            ["live_build_panel.corpus.remote_loaded"] = "endpoint:Low:None|build_count:High:None",
            ["live_build_panel.corpus.cache_write_degraded"] =
                "path:High:None|reason_code:Low:None",
            ["live_build_panel.live_snapshot.degraded"] =
                "section:Low:None|reason_code:Low:None|template_id:High:None|socket_id:High:None|item_size:Low:None",
            ["live_build_panel.card_preview.degraded"] =
                "operation:Low:None|reason_code:Low:None|template_id:High:None",
        };

        Assert.Equal(expected, actual);
        Assert.All(
            Definitions(),
            definition => Assert.Same(BppLogFeatureScope.LiveBuildPanel, definition.Scope)
        );
    }

    [Fact]
    public void Warning_storm_keys_are_only_the_locked_low_cardinality_fields()
    {
        Assert.Equal(
            [LiveBuildPanelLogEvents.CorpusDegradedReasonCode],
            LiveBuildPanelLogEvents.CorpusDegraded.StormPolicy!.KeyFields
        );
        Assert.Equal(
            [LiveBuildPanelLogEvents.CacheWriteDegradedReasonCode],
            LiveBuildPanelLogEvents.CacheWriteDegraded.StormPolicy!.KeyFields
        );
        Assert.Equal(
            [
                LiveBuildPanelLogEvents.LiveSnapshotDegradedSection,
                LiveBuildPanelLogEvents.LiveSnapshotDegradedReasonCode,
            ],
            LiveBuildPanelLogEvents.LiveSnapshotDegraded.StormPolicy!.KeyFields
        );
        Assert.Equal(
            [
                LiveBuildPanelLogEvents.CardPreviewDegradedOperation,
                LiveBuildPanelLogEvents.CardPreviewDegradedReasonCode,
            ],
            LiveBuildPanelLogEvents.CardPreviewDegraded.StormPolicy!.KeyFields
        );
        var validation = BppLogEventCatalog.FromDefinitions(Definitions().ToArray()).Validate();
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Violations.Select(violation => violation.ToString()))
        );
    }

    private static IEnumerable<BppLogEventDefinition> Definitions() =>
        typeof(LiveBuildPanelLogEvents)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!);

    private static string DescribeFields(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                string.Join(":", field.Name, field.Cardinality, field.Correlation)
            )
        );
}
