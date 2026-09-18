#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace Architecture.Tests;

public sealed class SettingsLogEventCatalogTests
{
    [Fact]
    public void Settings_events_match_the_locked_E15_E23_E28_manifest()
    {
        var definitions = Definitions(typeof(SettingsLogEvents))
            .Concat(Definitions(typeof(CombatStatusBarLogEvents)))
            .ToArray();
        var actual = definitions.ToDictionary(x => x.EventId, Describe, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat_status_bar.config.loaded"] = "speed_multiplier:Low:None",
            ["combat_status_bar.native_skin.ready"] = "",
            ["combat_status_bar.native_skin.unavailable"] = "",
            ["settings.hotkey.degraded"] =
                "action_id:Low:None|binding_path:High:Hash|reason_code:Low:None",
            ["settings.dock_sprite.degraded"] = "reason_code:Low:None|resource_id:Low:None",
            ["settings.native_section.degraded"] = "stage:Low:None|reason_code:Low:None",
            ["settings.native_section.recovered"] = "stage:Low:None",
            ["settings.native_section.layout_observed"] =
                "operation:Low:None|outcome:Low:None|affected_count:High:None|growth_units:High:None",
            ["settings.native_section.layout_degraded"] = "operation:Low:None|reason_code:Low:None",
            ["settings.native_section.layout_recovered"] = "operation:Low:None",
            ["settings.native_button.cloned"] = "button_id:Low:None",
            ["settings.keybind_rows.degraded"] = "stage:Low:None|reason_code:Low:None",
            ["settings.patch.degraded"] = "operation:Low:None|reason_code:Low:None",
            ["settings.row.layout_applied"] =
                "layout_mode:Low:None|row_id:Low:None|additional_index:High:None|step_px:High:None|position_x_px:High:None|position_y_px:High:None",
        };

        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected)
            Assert.Equal(pair.Value, actual[pair.Key]);
        var validation = BppLogEventCatalog.FromDefinitions(definitions).Validate();
        Assert.True(validation.IsValid, string.Join("; ", validation.Violations));
    }

    [Fact]
    public void Settings_warning_keys_are_closed_low_cardinality_fields()
    {
        AssertStorm(SettingsLogEvents.HotkeyDegraded, "reason_code");
        AssertStorm(SettingsLogEvents.DockSpriteDegraded, "reason_code");
        AssertStorm(SettingsLogEvents.NativeSectionDegraded, "stage", "reason_code");
        AssertStorm(SettingsLogEvents.NativeSectionLayoutDegraded, "operation", "reason_code");
        AssertStorm(SettingsLogEvents.KeybindRowsDegraded, "reason_code");
        AssertStorm(SettingsLogEvents.PatchDegraded, "operation", "reason_code");
    }

    [Fact]
    public void Health_tracker_recovers_only_the_same_operation_once()
    {
        var tracker = new OperationalHealthTracker<string, string>();

        Assert.True(tracker.ObserveFailure("footer", "missing"));
        Assert.False(tracker.ObserveFailure("footer", "different"));
        Assert.True(tracker.ObserveFailure("buffer", "missing"));

        Assert.True(tracker.ObserveSuccess("footer", out var footerReason));
        Assert.Equal("missing", footerReason);
        Assert.False(tracker.ObserveSuccess("footer", out _));
        Assert.True(tracker.ObserveSuccess("buffer", out var bufferReason));
        Assert.Equal("missing", bufferReason);

        Assert.True(tracker.ObserveFailure("footer", "missing"));
        tracker.Reset();
        Assert.True(tracker.ObserveFailure("footer", "missing"));
    }

    private static IEnumerable<BppLogEventDefinition> Definitions(Type type) =>
        type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(BppLogEventDefinition))
            .Select(field => (BppLogEventDefinition)field.GetValue(null)!);

    private static string Describe(BppLogEventDefinition definition) =>
        string.Join(
            "|",
            definition.Fields.Select(field =>
                $"{field.Name}:{field.Cardinality}:{field.Correlation}"
            )
        );

    private static void AssertStorm(BppLogEventDefinition definition, params string[] fields)
    {
        var policy = Assert.IsType<BppLogStormPolicy>(definition.StormPolicy);
        Assert.Equal(fields, policy.KeyFields.Select(field => field.Name));
        Assert.All(
            policy.KeyFields,
            field => Assert.Equal(BppLogCardinality.Low, field.Cardinality)
        );
    }
}
