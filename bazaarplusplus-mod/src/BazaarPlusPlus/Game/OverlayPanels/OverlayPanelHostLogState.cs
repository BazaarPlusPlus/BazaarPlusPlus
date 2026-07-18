#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.OverlayPanels;

internal sealed class OverlayPanelHostLogState
{
    private readonly object _sync = new();
    private readonly HashSet<string> _tickDegradedPanels = new(StringComparer.Ordinal);
    private bool _combatProbeDegraded;

    internal void ExecuteTick(string panelId, Action callback)
    {
        try
        {
            callback();
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
                OverlayPanelLogEvents.DirectiveFailed,
                ex,
                OverlayPanelLogEvents.DirectiveFailedRequestId.Bind(requestId),
                OverlayPanelLogEvents.DirectiveFailedPanelId.Bind(panelId),
                OverlayPanelLogEvents.DirectiveFailedDirective.Bind(directive),
                OverlayPanelLogEvents.DirectiveFailedReasonCode.Bind(
                    OverlayDirectiveFailureReasonCode.CallbackException
                )
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
            OverlayPanelLogEvents.TickDegraded,
            exception,
            OverlayPanelLogEvents.TickDegradedPanelId.Bind(panelId),
            OverlayPanelLogEvents.TickDegradedReasonCode.Bind(
                OverlayTickFailureReasonCode.CallbackException
            )
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
            OverlayPanelLogEvents.TickRecovered,
            OverlayPanelLogEvents.TickRecoveredPanelId.Bind(panelId)
        );
    }

    private static void RecoverTickStorm(string panelId) =>
        BppLog.RecoverStorm(
            OverlayPanelLogEvents.TickDegraded,
            OverlayPanelLogEvents.TickDegradedPanelId.Bind(panelId),
            OverlayPanelLogEvents.TickDegradedReasonCode.Bind(
                OverlayTickFailureReasonCode.CallbackException
            )
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
            OverlayPanelLogEvents.CombatProbeDegraded,
            exception,
            OverlayPanelLogEvents.CombatProbeDegradedReasonCode.Bind(
                OverlayCombatProbeFailureReasonCode.ReadFailed
            )
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
            OverlayPanelLogEvents.CombatProbeDegraded,
            OverlayPanelLogEvents.CombatProbeDegradedReasonCode.Bind(
                OverlayCombatProbeFailureReasonCode.ReadFailed
            )
        );
        BppLog.InfoEvent(OverlayPanelLogEvents.CombatProbeRecovered);
    }
}
