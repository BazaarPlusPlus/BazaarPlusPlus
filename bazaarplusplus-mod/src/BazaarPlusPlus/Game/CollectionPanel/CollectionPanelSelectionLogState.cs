#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CollectionPanel;

internal readonly record struct CollectionPanelSelectionProbeFailure(
    CollectionPanelSelectionProbe Probe,
    CollectionPanelLogReasonCode ReasonCode,
    Exception Exception
);

internal readonly struct CollectionPanelSelectionOpenObservation
{
    private CollectionPanelSelectionOpenObservation(
        bool semanticallyComplete,
        IReadOnlyList<CollectionPanelSelectionProbeFailure> failures
    )
    {
        IsSemanticallyComplete = semanticallyComplete;
        Failures = failures;
    }

    internal bool IsSemanticallyComplete { get; }
    internal IReadOnlyList<CollectionPanelSelectionProbeFailure> Failures { get; }

    internal static CollectionPanelSelectionOpenObservation Complete() => new(true, []);

    internal static CollectionPanelSelectionOpenObservation Degraded(
        IReadOnlyList<CollectionPanelSelectionProbeFailure> failures
    ) => new(false, failures ?? []);
}

internal sealed class CollectionPanelSelectionLogState
{
    private CollectionPanelSelectionProbeFailure? _firstFailure;

    internal void ObserveOpen(CollectionPanelSelectionOpenObservation observation)
    {
        if (_firstFailure.HasValue)
        {
            if (!observation.IsSemanticallyComplete || observation.Failures.Count != 0)
                return;

            var recovered = _firstFailure.Value;
            _firstFailure = null;
            BppLog.RecoverStorm(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.selection.degraded",
                    storm: ["probe", "reason_code"]
                ),
                ("probe", recovered.Probe),
                ("reason_code", recovered.ReasonCode)
            );
            BppLog.InfoEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CollectionPanel,
                    "collection_panel.selection.recovered"
                ),
                ("probe", recovered.Probe)
            );
            return;
        }

        if (observation.Failures.Count == 0)
            return;

        var failure = observation.Failures[0];
        _firstFailure = failure;
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.CollectionPanel,
                "collection_panel.selection.degraded",
                storm: ["probe", "reason_code"]
            ),
            failure.Exception,
            ("probe", failure.Probe),
            ("reason_code", failure.ReasonCode)
        );
    }
}
