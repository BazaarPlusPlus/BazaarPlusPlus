#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class CombatReplayLogEventCatalogTests
{
    private const string PlaybackTerminalSchema =
        "battle_id:High:Short|source:Low:None|end_reason_code:Low:None|duration_ms:High:None|reason_code:Low:None|degradation_count:Low:None|rollback_status:Low:None";

    [Fact]
    public void Playback_persistence_and_warmup_events_match_the_locked_manifest_schemas()
    {
        var actual = Definitions()
            .ToDictionary(definition => definition.EventId, DescribeFields, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat_replay.capture.failed"] = "run_id:High:Short|reason_code:Low:None",
            ["combat_replay.current_recording_ui.observed"] =
                "phase:Low:None|snapshot_visible:Low:None|layout_available:Low:None|layout_reason_code:Low:None|clone_active:Low:None|native_replay_bound:Low:None|icon_available:Low:None",
            ["combat_replay.current_recording.presentation_gate_resolved"] =
                "recording_id:High:Short|outcome:Low:None|expected_items:Low:None|visible_items:Low:None|face_up_items:Low:None|settled_items:Low:None|expected_skills:Low:None|registered_skills:Low:None|ready_skills:Low:None|elapsed_ms:High:None",
            ["combat_replay.playback.request_rejected"] =
                "source:Low:None|reason_code:Low:None|battle_id:High:Short",
            ["combat_replay.playback.started"] =
                "battle_id:High:Short|source:Low:None|record_video:Low:None",
            ["combat_replay.playback.succeeded"] = PlaybackTerminalSchema,
            ["combat_replay.playback.degraded"] = PlaybackTerminalSchema,
            ["combat_replay.playback.failed"] = PlaybackTerminalSchema,
            ["combat_replay.persistence.failed"] =
                "battle_id:High:Short|run_id:High:Short|reason_code:Low:None",
            ["combat_replay.persistence.succeeded"] =
                "battle_id:High:Short|run_id:High:Short|reason_code:Low:None",
            ["combat_replay.maintenance.completed"] =
                "reason_code:Low:None|evaluated_count:High:None|scheduled_count:High:None|deleted_count:High:None|missing_count:High:None|orphan_count:High:None|failed_count:High:None",
            ["combat_replay.maintenance.degraded"] =
                "reason_code:Low:None|evaluated_count:High:None|scheduled_count:High:None|deleted_count:High:None|missing_count:High:None|orphan_count:High:None|failed_count:High:None",
            ["combat_replay.persistence.shutdown_incomplete"] =
                "pending_count:High:None|in_flight:Low:None|timeout_ms:High:None",
            ["combat_replay.persistence.rollback_cleanup_failed"] = "battle_id:High:Short",
            ["combat_replay.playback.cleanup_observed"] =
                "stage:Low:None|removed_count:High:None|battle_id:High:Short",
            ["combat_replay.warmup.completed"] =
                "stage:Low:None|battle_id:High:Short|duration_ms:High:None|board_bank_loaded_count:High:None|board_bank_already_loaded_count:High:None|board_bank_failed_count:High:None|board_bank_skipped_count:High:None|soundtrack_bank_loaded_count:High:None|soundtrack_bank_already_loaded_count:High:None|soundtrack_bank_failed_count:High:None|soundtrack_bank_skipped_count:High:None|shared_asset_preloaded_count:High:None|shared_asset_skipped_count:High:None|card_preloaded_count:High:None|card_skipped_count:High:None|card_failed_count:High:None|override_asset_preloaded_count:High:None|override_asset_skipped_count:High:None|override_asset_failed_count:High:None|vfx_prewarmed_count:High:None|vfx_skipped_count:High:None|vfx_failed_count:High:None",
            ["combat_replay.warmup.asset_skipped"] =
                "stage:Low:None|asset_key:High:None|reason_code:Low:None",
        };

        Assert.Equal(expected.Count, actual.Count);
        foreach (var (eventId, schema) in expected)
        {
            Assert.True(actual.TryGetValue(eventId, out var actualSchema), $"Missing {eventId}.");
            Assert.Equal(schema, actualSchema);
        }

        Assert.All(
            Definitions(),
            definition => Assert.Same(BppLogFeatureScope.CombatReplay, definition.Scope)
        );
    }

    [Fact]
    public void Event_source_is_discoverable_and_every_definition_is_a_direct_readonly_field()
    {
        var source = typeof(CombatReplayLogEvents);
        Assert.NotNull(source.GetCustomAttribute<BppLogEventSourceAttribute>());

        var fields = source
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .ToArray();
        Assert.NotEmpty(fields);
        Assert.All(
            fields,
            field => Assert.True(field.IsInitOnly, $"{field.Name} must be readonly.")
        );

        var validation = BppLogEventCatalog
            .FromDefinitions(
                fields.Select(field => (BppLogEventDefinition)field.GetValue(null)!).ToArray()
            )
            .Validate();
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Violations.Select(violation => violation.ToString()))
        );
    }

    private static IEnumerable<BppLogEventDefinition> Definitions() =>
        typeof(CombatReplayLogEvents)
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
