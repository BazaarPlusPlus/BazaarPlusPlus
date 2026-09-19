#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace OverlayPanelLogging.Tests;

public sealed class OverlayPanelLogEventCatalogTests
{
    [Fact]
    public void Events_match_the_locked_D60_to_D62_schemas()
    {
        var actual = Definitions()
            .ToDictionary(definition => definition.EventId, DescribeFields, StringComparer.Ordinal);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["overlay_panels.host.tick_degraded"] = "panel_id:Low:None|reason_code:Low:None",
            ["overlay_panels.host.tick_recovered"] = "panel_id:Low:None",
            ["overlay_panels.directive.failed"] =
                "request_id:High:Short|panel_id:Low:None|directive:Low:None|reason_code:Low:None",
            ["overlay_panels.combat_probe.degraded"] = "reason_code:Low:None",
            ["overlay_panels.combat_probe.recovered"] = "",
        };

        Assert.Equal(expected, actual);
        Assert.All(
            Definitions(),
            definition => Assert.Same(BppLogFeatureScope.OverlayPanels, definition.Scope)
        );
        var validation = BppLogEventCatalog.FromDefinitions(Definitions().ToArray()).Validate();
        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Violations.Select(violation => violation.ToString()))
        );
    }

    [Fact]
    public void Warning_storm_keys_match_panel_and_reason_policy()
    {
        Assert.Equal(
            [
                OverlayPanelLogEvents.TickDegradedPanelId,
                OverlayPanelLogEvents.TickDegradedReasonCode,
            ],
            OverlayPanelLogEvents.TickDegraded.StormPolicy!.KeyFields
        );
        Assert.Equal(
            [OverlayPanelLogEvents.CombatProbeDegradedReasonCode],
            OverlayPanelLogEvents.CombatProbeDegraded.StormPolicy!.KeyFields
        );
        Assert.Null(OverlayPanelLogEvents.DirectiveFailed.StormPolicy);
    }

    private static IEnumerable<BppLogEventDefinition> Definitions() =>
        typeof(OverlayPanelLogEvents)
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
