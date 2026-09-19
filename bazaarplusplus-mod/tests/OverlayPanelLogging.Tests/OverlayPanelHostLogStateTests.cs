#nullable enable
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace OverlayPanelLogging.Tests;

public sealed class OverlayPanelHostLogStateTests : IDisposable
{
    public OverlayPanelHostLogStateTests() => BppLog.Reset();

    public void Dispose() => BppLog.Reset();

    [Fact]
    public void Repeated_tick_failures_warn_once_per_panel_and_first_success_recovers()
    {
        var state = new OverlayPanelHostLogState();
        var otherPanelTicks = 0;

        state.ExecuteTick(
            "LiveBuildPanel",
            (_, _) => throw new InvalidOperationException("first"),
            0f,
            false
        );
        state.ExecuteTick(
            "LiveBuildPanel",
            (_, _) => throw new InvalidOperationException("second"),
            0f,
            false
        );
        state.ExecuteTick("HistoryPanel", (_, _) => otherPanelTicks++, 0f, false);
        state.ExecuteTick("LiveBuildPanel", (_, _) => { }, 0f, false);
        state.ExecuteTick("LiveBuildPanel", (_, _) => { }, 0f, false);

        Assert.Equal(1, otherPanelTicks);
        Assert.Equal(
            ["overlay_panels.host.tick_degraded", "overlay_panels.host.tick_recovered"],
            BppLog.Events.Select(item => item.Definition.EventId)
        );
        Assert.Single(BppLog.StormRecoveries);
    }

    [Fact]
    public void Unregister_forgets_tick_episode_without_emitting_recovery_Info()
    {
        var state = new OverlayPanelHostLogState();
        state.ExecuteTick(
            "LiveBuildPanel",
            (_, _) => throw new InvalidOperationException("failure"),
            0f,
            false
        );
        BppLog.Reset();

        state.ForgetPanel("LiveBuildPanel");
        state.ExecuteTick("LiveBuildPanel", (_, _) => { }, 0f, false);

        Assert.Empty(BppLog.Events);
        Assert.Single(BppLog.StormRecoveries);
    }

    [Fact]
    public void Open_directive_has_one_request_correlated_terminal() =>
        AssertDirectiveTerminal(OverlayDirectiveKind.Open);

    [Fact]
    public void Close_directive_has_one_request_correlated_terminal() =>
        AssertDirectiveTerminal(OverlayDirectiveKind.Close);

    [Fact]
    public void Scene_change_notification_has_one_request_correlated_terminal() =>
        AssertDirectiveTerminal(OverlayDirectiveKind.NotifySceneChanged);

    private static void AssertDirectiveTerminal(OverlayDirectiveKind directive)
    {
        var state = new OverlayPanelHostLogState();
        var requestId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

        state.ExecuteDirective(
            requestId,
            "LiveBuildPanel",
            directive,
            () => throw new InvalidOperationException("callback failed")
        );

        var error = Assert.Single(BppLog.Events);
        Assert.Equal("Error", error.Severity);
        Assert.Equal("overlay_panels.directive.failed", error.Definition.EventId);
        Assert.Equal(requestId, Value(error, "request_id"));
        Assert.Equal(directive, Value(error, "directive"));
    }

    [Fact]
    public void Directive_failure_is_contained_so_later_callbacks_can_continue()
    {
        var state = new OverlayPanelHostLogState();
        var continued = false;

        state.ExecuteDirective(
            Guid.NewGuid(),
            "First",
            OverlayDirectiveKind.Close,
            () => throw new InvalidOperationException("failure")
        );
        state.ExecuteDirective(
            Guid.NewGuid(),
            "Second",
            OverlayDirectiveKind.Open,
            () => continued = true
        );

        Assert.True(continued);
        Assert.Single(BppLog.Events);
    }

    [Fact]
    public void Combat_probe_falls_back_false_warns_once_and_recovers_once()
    {
        var state = new OverlayPanelHostLogState();
        bool Throw() => throw new InvalidOperationException("probe failure");

        Assert.False(state.ReadIsInCombat(Throw));
        Assert.False(state.ReadIsInCombat(Throw));
        Assert.True(state.ReadIsInCombat(() => true));
        Assert.False(state.ReadIsInCombat(() => false));

        Assert.Equal(
            ["overlay_panels.combat_probe.degraded", "overlay_panels.combat_probe.recovered"],
            BppLog.Events.Select(item => item.Definition.EventId)
        );
        Assert.Single(BppLog.StormRecoveries);
    }

    private static object? Value(CapturedBppLogEvent captured, string fieldName) =>
        captured.Values.Single(value => value.Field.Name == fieldName).Value;
}
