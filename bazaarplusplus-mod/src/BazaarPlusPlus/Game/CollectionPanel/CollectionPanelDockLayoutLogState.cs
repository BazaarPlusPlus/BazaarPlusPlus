#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal readonly record struct CollectionPanelDockLayoutObservation(
    bool IsAvailable,
    CollectionPanelLogReasonCode? ReasonCode,
    string? Blocker
)
{
    internal static CollectionPanelDockLayoutObservation Available() => new(true, null, null);

    internal static CollectionPanelDockLayoutObservation Degraded(
        CollectionPanelLogReasonCode reasonCode,
        string? blocker
    ) => new(false, reasonCode, blocker);
}

internal sealed class CollectionPanelDockLayoutLogState
{
    private CollectionPanelDockLayoutObservation? _degradation;

    internal void Observe(CollectionPanelDockLayoutObservation observation)
    {
        if (observation.IsAvailable)
        {
            if (!_degradation.HasValue)
                return;

            var recovered = _degradation.Value;
            _degradation = null;
            BppLog.RecoverStorm(
                CollectionPanelLogEvents.DockLayoutDegraded,
                CollectionPanelLogEvents.DockLayoutDegradedReasonCode.Bind(recovered.ReasonCode)
            );
            BppLog.InfoEvent(
                CollectionPanelLogEvents.DockLayoutRecovered,
                CollectionPanelLogEvents.DockLayoutRecoveredReasonCode.Bind(recovered.ReasonCode),
                CollectionPanelLogEvents.DockLayoutRecoveredBlocker.Bind(recovered.Blocker)
            );
            return;
        }

        if (_degradation.HasValue || !observation.ReasonCode.HasValue)
            return;

        _degradation = observation;
        BppLog.WarnEvent(
            CollectionPanelLogEvents.DockLayoutDegraded,
            CollectionPanelLogEvents.DockLayoutDegradedReasonCode.Bind(observation.ReasonCode),
            CollectionPanelLogEvents.DockLayoutDegradedBlocker.Bind(observation.Blocker)
        );
    }
}
