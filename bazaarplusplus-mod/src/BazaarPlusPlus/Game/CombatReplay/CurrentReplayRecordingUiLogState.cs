#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay;

internal enum CurrentReplayRecordingUiLayoutReasonCode
{
    None,
    MissingButton,
    AnchorFootprintUnavailable,
    TargetFootprintUnavailable,
    AnchorCanvasUnavailable,
    TargetPositionUnavailable,
    ViewportUnavailable,
    Obstructed,
    Unknown,
}

internal readonly record struct CurrentReplayRecordingUiObservation(
    CurrentReplayRecordingPhase Phase,
    bool SnapshotVisible,
    bool LayoutAvailable,
    CurrentReplayRecordingUiLayoutReasonCode LayoutReasonCode,
    bool CloneActive,
    bool NativeActionsBound,
    bool IconAvailable
);

internal sealed class CurrentReplayRecordingUiLogState
{
    private CurrentReplayRecordingUiObservation? _lastObservation;

    internal void Observe(
        CurrentReplayRecordingSnapshot snapshot,
        bool layoutAvailable,
        CurrentReplayRecordingUiLayoutReasonCode layoutReasonCode,
        bool cloneActive,
        bool nativeActionsBound,
        bool iconAvailable
    )
    {
        var observation = new CurrentReplayRecordingUiObservation(
            snapshot.Phase,
            snapshot.Visible,
            layoutAvailable,
            layoutReasonCode,
            cloneActive,
            nativeActionsBound,
            iconAvailable
        );
        if (_lastObservation == observation)
            return;

        _lastObservation = observation;
        BppLog.InfoEvent(
            CombatReplayLogEvents.CurrentRecordingUiObserved,
            CombatReplayLogEvents.CurrentRecordingUiPhase.Bind(observation.Phase),
            CombatReplayLogEvents.CurrentRecordingUiSnapshotVisible.Bind(
                observation.SnapshotVisible
            ),
            CombatReplayLogEvents.CurrentRecordingUiLayoutAvailable.Bind(
                observation.LayoutAvailable
            ),
            CombatReplayLogEvents.CurrentRecordingUiLayoutReasonCode.Bind(
                observation.LayoutReasonCode
            ),
            CombatReplayLogEvents.CurrentRecordingUiCloneActive.Bind(observation.CloneActive),
            CombatReplayLogEvents.CurrentRecordingUiNativeReplayBound.Bind(
                observation.NativeActionsBound
            ),
            CombatReplayLogEvents.CurrentRecordingUiIconAvailable.Bind(observation.IconAvailable)
        );
    }

    internal static CurrentReplayRecordingUiLayoutReasonCode ResolveLayoutReason(
        bool layoutAvailable,
        string? blockerName
    )
    {
        if (layoutAvailable)
            return CurrentReplayRecordingUiLayoutReasonCode.None;

        return blockerName switch
        {
            "missing-collection-button" => CurrentReplayRecordingUiLayoutReasonCode.MissingButton,
            "gear-footprint-unavailable" =>
                CurrentReplayRecordingUiLayoutReasonCode.AnchorFootprintUnavailable,
            "collection-footprint-unavailable" =>
                CurrentReplayRecordingUiLayoutReasonCode.TargetFootprintUnavailable,
            "anchor-canvas-unavailable" =>
                CurrentReplayRecordingUiLayoutReasonCode.AnchorCanvasUnavailable,
            "target-local-position-unavailable" =>
                CurrentReplayRecordingUiLayoutReasonCode.TargetPositionUnavailable,
            "viewport" => CurrentReplayRecordingUiLayoutReasonCode.ViewportUnavailable,
            null => CurrentReplayRecordingUiLayoutReasonCode.Unknown,
            _ => CurrentReplayRecordingUiLayoutReasonCode.Obstructed,
        };
    }
}
