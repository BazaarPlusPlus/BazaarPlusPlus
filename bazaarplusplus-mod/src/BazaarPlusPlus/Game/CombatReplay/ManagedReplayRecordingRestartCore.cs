#nullable enable
using BazaarPlusPlus.GameInterop.CombatReplay;

namespace BazaarPlusPlus.Game.CombatReplay;

internal enum ManagedReplayRecordingRestartAction
{
    None,
    Blocked,
    Promote,
    PublishStarting,
    InvokeNativeReplay,
    CompleteStarted,
    CompleteFailed,
}

internal readonly record struct ManagedReplayRecordingRestartDecision(
    ManagedReplayRecordingRestartAction Action,
    CurrentReplayRecordingStatusCode StatusCode,
    ReplayPlaybackReasonCode FailureReasonCode,
    string? EndReason
)
{
    internal static ManagedReplayRecordingRestartDecision ForAction(
        ManagedReplayRecordingRestartAction action
    ) => new(action, CurrentReplayRecordingStatusCode.None, ReplayPlaybackReasonCode.None, null);

    internal static ManagedReplayRecordingRestartDecision Blocked(
        CurrentReplayRecordingStatusCode statusCode
    ) =>
        new(
            ManagedReplayRecordingRestartAction.Blocked,
            statusCode,
            ReplayPlaybackReasonCode.None,
            null
        );

    internal static ManagedReplayRecordingRestartDecision Failed(
        CurrentReplayRecordingStatusCode statusCode,
        ReplayPlaybackReasonCode reasonCode,
        string endReason
    ) => new(ManagedReplayRecordingRestartAction.CompleteFailed, statusCode, reasonCode, endReason);
}

/// <summary>
/// Pure transaction state for promoting one completed managed replay into a recorded restart.
/// Runtime mutations happen only when the returned action requests them; every failure after the
/// promotion boundary converges on one terminal action.
/// </summary>
internal sealed class ManagedReplayRecordingRestartCore
{
    private ManagedReplayRecordingRestartPhase _phase;

    internal ManagedReplayRecordingRestartDecision Begin(
        bool sessionAvailable,
        bool recorderAvailable,
        NativeReplayRestartBlocker nativeBlocker
    )
    {
        if (_phase != ManagedReplayRecordingRestartPhase.AwaitingPreflight)
            return default;
        if (!sessionAvailable)
            return ManagedReplayRecordingRestartDecision.Blocked(
                CurrentReplayRecordingStatusCode.SessionChanged
            );
        if (nativeBlocker != NativeReplayRestartBlocker.None)
        {
            return ManagedReplayRecordingRestartDecision.Blocked(
                CurrentReplayRecordingStatusCodes.FromNativeBlocker(nativeBlocker)
            );
        }
        if (!recorderAvailable)
            return ManagedReplayRecordingRestartDecision.Blocked(
                CurrentReplayRecordingStatusCode.RecorderUnavailable
            );

        _phase = ManagedReplayRecordingRestartPhase.AwaitingPromotion;
        return ManagedReplayRecordingRestartDecision.ForAction(
            ManagedReplayRecordingRestartAction.Promote
        );
    }

    internal ManagedReplayRecordingRestartDecision OnPromoted(
        bool operationPromoted,
        bool publisherPromoted
    )
    {
        if (_phase != ManagedReplayRecordingRestartPhase.AwaitingPromotion)
            return default;
        if (operationPromoted && publisherPromoted)
        {
            _phase = ManagedReplayRecordingRestartPhase.AwaitingStartingPublish;
            return ManagedReplayRecordingRestartDecision.ForAction(
                ManagedReplayRecordingRestartAction.PublishStarting
            );
        }

        return Fail(
            CurrentReplayRecordingStatusCode.PromotionFailed,
            ReplayPlaybackReasonCode.RecordingRestartPromotionFailed,
            "recording-restart-promotion-failed"
        );
    }

    internal ManagedReplayRecordingRestartDecision OnStartingPublished(bool succeeded)
    {
        if (_phase != ManagedReplayRecordingRestartPhase.AwaitingStartingPublish)
            return default;
        if (succeeded)
        {
            _phase = ManagedReplayRecordingRestartPhase.AwaitingNativeInvocation;
            return ManagedReplayRecordingRestartDecision.ForAction(
                ManagedReplayRecordingRestartAction.InvokeNativeReplay
            );
        }

        return Fail(
            CurrentReplayRecordingStatusCode.StartingPublishFailed,
            ReplayPlaybackReasonCode.RecordingRestartPublishFailed,
            "recording-restart-publish-failed"
        );
    }

    internal ManagedReplayRecordingRestartDecision OnNativeInvoked(
        bool invocationSucceeded,
        bool replayStarted
    )
    {
        if (_phase != ManagedReplayRecordingRestartPhase.AwaitingNativeInvocation)
            return default;
        if (!invocationSucceeded)
        {
            return Fail(
                CurrentReplayRecordingStatusCode.NativeInvokeFailed,
                ReplayPlaybackReasonCode.RecordingRestartInvokeFailed,
                "recording-restart-invoke-failed"
            );
        }
        if (!replayStarted)
        {
            return Fail(
                CurrentReplayRecordingStatusCode.NativeStartRejected,
                ReplayPlaybackReasonCode.RecordingRestartRejected,
                "recording-restart-not-started"
            );
        }

        _phase = ManagedReplayRecordingRestartPhase.Terminal;
        return ManagedReplayRecordingRestartDecision.ForAction(
            ManagedReplayRecordingRestartAction.CompleteStarted
        );
    }

    private ManagedReplayRecordingRestartDecision Fail(
        CurrentReplayRecordingStatusCode statusCode,
        ReplayPlaybackReasonCode reasonCode,
        string endReason
    )
    {
        _phase = ManagedReplayRecordingRestartPhase.Terminal;
        return ManagedReplayRecordingRestartDecision.Failed(statusCode, reasonCode, endReason);
    }

    private enum ManagedReplayRecordingRestartPhase
    {
        AwaitingPreflight,
        AwaitingPromotion,
        AwaitingStartingPublish,
        AwaitingNativeInvocation,
        Terminal,
    }
}
