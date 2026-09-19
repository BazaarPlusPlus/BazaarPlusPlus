#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace HistoryPanelOperationalLogging.Tests;

public sealed class HistoryPanelLogCatalogTests
{
    [Fact]
    public void History_events_match_the_locked_D28_through_D42_schemas()
    {
        var actual = Definitions().ToDictionary(item => item.EventId, Describe);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["history_panel.mount.failed"] = "dependency:Low:None|reason_code:Low:None",
            ["history_panel.replay.preflight_completed"] =
                "request_id:High:Short|battle_id:High:Short|record_video:Low:None|can_record:Low:None|reason_code:Low:None",
            ["history_panel.replay.failed"] =
                "request_id:High:Short|battle_id:High:Short|record_video:Low:None|reason_code:Low:None",
            ["history_panel.replay.accepted"] =
                "request_id:High:Short|battle_id:High:Short|record_video:Low:None",
            ["history_panel.run_delete.failed"] = DeleteSchema,
            ["history_panel.run_delete.degraded"] = DeleteSchema,
            ["history_panel.run_delete.succeeded"] = DeleteSchema,
            ["history_panel.server_health.failed"] = HealthSchema,
            ["history_panel.server_health.succeeded"] = HealthSchema,
            ["history_panel.ghost_sync.failed"] = SyncSchema,
            ["history_panel.ghost_sync.succeeded"] = SyncSchema,
            ["history_panel.ghost_identity.read_failed"] = "reason_code:Low:None",
            ["history_panel.preview.socket_effect_degraded"] =
                "template_id:High:None|reason_code:Low:None",
            ["history_panel.preview.static_data_degraded"] = "reason_code:Low:None",
            ["history_panel.preview.payload_degraded"] =
                "battle_id:High:Short|reason_code:Low:None",
            ["history_panel.open.failed"] = "reason_code:Low:None",
            ["history_panel.open.skipped"] = "reason_code:Low:None",
            ["history_panel.card_preview.degraded"] =
                "operation:Low:None|reason_code:Low:None|template_id:High:None",
        };

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected, actual);
        Assert.All(
            Definitions(),
            definition => Assert.Same(BppLogFeatureScope.HistoryPanel, definition.Scope)
        );
    }

    [Fact]
    public void Degradation_storm_keys_use_only_low_cardinality_fields()
    {
        var stormed = Definitions()
            .Where(definition => definition.StormPolicy != null)
            .ToDictionary(definition => definition.EventId);

        Assert.Equal(
            new[]
            {
                "history_panel.card_preview.degraded",
                "history_panel.preview.payload_degraded",
                "history_panel.preview.socket_effect_degraded",
                "history_panel.preview.static_data_degraded",
            },
            stormed.Keys.OrderBy(value => value, StringComparer.Ordinal)
        );
        Assert.All(
            stormed.Values,
            definition =>
            {
                Assert.All(
                    definition.StormPolicy!.KeyFields,
                    key =>
                    {
                        Assert.Contains(key.Name, new[] { "operation", "reason_code" });
                        Assert.Equal(BppLogCardinality.Low, key.Cardinality);
                        Assert.Equal(BppLogCorrelationPolicy.None, key.Correlation);
                    }
                );
            }
        );
    }

    [Fact]
    public void Event_source_is_discoverable_and_valid()
    {
        Assert.NotNull(
            typeof(HistoryPanelLogEvents).GetCustomAttribute<BppLogEventSourceAttribute>()
        );
        var validation = BppLogEventCatalog.FromDefinitions(Definitions().ToArray()).Validate();
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Violations.Select(violation => violation.ToString()))
        );
    }

    private const string DeleteSchema =
        "request_id:High:Short|run_id:High:Short|battle_count:High:None|cleanup_failed_count:High:None|reason_code:Low:None";
    private const string HealthSchema =
        "request_id:High:Short|duration_ms:High:None|reason_code:Low:None";
    private const string SyncSchema =
        "request_id:High:Short|imported_count:High:None|reason_code:Low:None";

    private static IEnumerable<BppLogEventDefinition> Definitions() =>
        typeof(HistoryPanelLogEvents)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!);

    private static string Describe(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Cardinality}:{field.Correlation}"
            )
        );
}
