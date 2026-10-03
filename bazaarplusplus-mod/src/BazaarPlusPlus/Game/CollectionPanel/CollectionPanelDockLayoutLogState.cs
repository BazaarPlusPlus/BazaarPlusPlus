#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

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
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.dock_layout.degraded",
                    storm: ["reason_code"]
                ),
                ("reason_code", recovered.ReasonCode)
            );
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.dock_layout.recovered"
                ),
                ("reason_code", recovered.ReasonCode),
                ("blocker", recovered.Blocker)
            );
            return;
        }

        if (_degradation.HasValue || !observation.ReasonCode.HasValue)
            return;

        _degradation = observation;
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.CollectionPanel,
                "collection_panel.dock_layout.degraded",
                storm: ["reason_code"]
            ),
            ("reason_code", observation.ReasonCode),
            ("blocker", observation.Blocker)
        );
    }
}
