#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.OverlayPanels;

internal sealed class OverlayPanelHostLogState
{
    private readonly object _sync = new();
    private readonly HashSet<string> _tickDegradedPanels = new(StringComparer.Ordinal);
    private bool _combatProbeDegraded;

    // dt/isOpen are passed by value and the tick delegate is the registration's own stored
    // delegate, so the per-frame dispatch allocates nothing.
    internal void ExecuteTick(string panelId, Action<float, bool> tick, float dt, bool isOpen)
    {
        try
        {
            tick(dt, isOpen);
        }
        catch (Exception ex)
        {
            ReportTickDegraded(panelId, ex);
            return;
        }

        ReportTickRecovered(panelId);
    }

    internal void ExecuteDirective(
        Guid requestId,
        string panelId,
        OverlayDirectiveKind directive,
        Action callback
    )
    {
        try
        {
            callback();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.OverlayPanels,
                    "overlay_panels.directive.failed"
                ),
                ex,
                ("request_id", requestId, BppLogCorrelationPolicy.Short),
                ("panel_id", panelId),
                ("directive", directive),
                ("reason_code", OverlayDirectiveFailureReasonCode.CallbackException)
            );
        }
    }

    internal bool ReadIsInCombat(Func<bool> read)
    {
        try
        {
            var isInCombat = read();
            ReportCombatProbeRecovered();
            return isInCombat;
        }
        catch (Exception ex)
        {
            ReportCombatProbeDegraded(ex);
            return false;
        }
    }

    internal void ForgetPanel(string panelId)
    {
        lock (_sync)
        {
            if (!_tickDegradedPanels.Remove(panelId))
                return;
        }
        RecoverTickStorm(panelId);
    }

    private void ReportTickDegraded(string panelId, Exception exception)
    {
        lock (_sync)
        {
            if (!_tickDegradedPanels.Add(panelId))
                return;
        }

        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.OverlayPanels,
                "overlay_panels.host.tick_degraded",
                storm: ["panel_id", "reason_code"]
            ),
            exception,
            ("panel_id", panelId),
            ("reason_code", OverlayTickFailureReasonCode.CallbackException)
        );
    }

    private void ReportTickRecovered(string panelId)
    {
        lock (_sync)
        {
            if (!_tickDegradedPanels.Remove(panelId))
                return;
        }

        RecoverTickStorm(panelId);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.OverlayPanels, "overlay_panels.host.tick_recovered"),
            ("panel_id", panelId)
        );
    }

    private static void RecoverTickStorm(string panelId) =>
        BppLog.RecoverStorm(
            new BppLogEvent(
                BppLogFeatureScope.OverlayPanels,
                "overlay_panels.host.tick_degraded",
                storm: ["panel_id", "reason_code"]
            ),
            ("panel_id", panelId),
            ("reason_code", OverlayTickFailureReasonCode.CallbackException)
        );

    private void ReportCombatProbeDegraded(Exception exception)
    {
        lock (_sync)
        {
            if (_combatProbeDegraded)
                return;
            _combatProbeDegraded = true;
        }

        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.OverlayPanels,
                "overlay_panels.combat_probe.degraded",
                storm: ["reason_code"]
            ),
            exception,
            ("reason_code", OverlayCombatProbeFailureReasonCode.ReadFailed)
        );
    }

    private void ReportCombatProbeRecovered()
    {
        lock (_sync)
        {
            if (!_combatProbeDegraded)
                return;
            _combatProbeDegraded = false;
        }

        BppLog.RecoverStorm(
            new BppLogEvent(
                BppLogFeatureScope.OverlayPanels,
                "overlay_panels.combat_probe.degraded",
                storm: ["reason_code"]
            ),
            ("reason_code", OverlayCombatProbeFailureReasonCode.ReadFailed)
        );
        BppLog.InfoEvent(
            new BppLogEvent(
                BppLogFeatureScope.OverlayPanels,
                "overlay_panels.combat_probe.recovered"
            )
        );
    }
}
