#nullable enable
using BazaarPlusPlus.Infrastructure;

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

    internal static CollectionPanelSelectionOpenObservation Incomplete() => new(false, []);

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
                CollectionPanelLogEvents.SelectionDegraded,
                CollectionPanelLogEvents.SelectionDegradedProbe.Bind(recovered.Probe),
                CollectionPanelLogEvents.SelectionDegradedReasonCode.Bind(recovered.ReasonCode)
            );
            BppLog.InfoEvent(
                CollectionPanelLogEvents.SelectionRecovered,
                CollectionPanelLogEvents.SelectionRecoveredProbe.Bind(recovered.Probe)
            );
            return;
        }

        if (observation.Failures.Count == 0)
            return;

        var failure = observation.Failures[0];
        _firstFailure = failure;
        BppLog.WarnEvent(
            CollectionPanelLogEvents.SelectionDegraded,
            failure.Exception,
            CollectionPanelLogEvents.SelectionDegradedProbe.Bind(failure.Probe),
            CollectionPanelLogEvents.SelectionDegradedReasonCode.Bind(failure.ReasonCode)
        );
    }
}
