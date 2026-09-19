using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.GameInterop.CombatReplay;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class ManagedReplayRecordingRestartCoreTests
{
    [Fact]
    public void Predictable_native_blocker_does_not_cross_promotion_boundary()
    {
        var blockers = new[]
        {
            NativeReplayRestartBlocker.ReplayInProgress,
            NativeReplayRestartBlocker.StorageMoving,
            NativeReplayRestartBlocker.StorageOpen,
            NativeReplayRestartBlocker.InputBlocked,
            NativeReplayRestartBlocker.ConnectionLost,
        };

        foreach (var blocker in blockers)
        {
            var core = new ManagedReplayRecordingRestartCore();
            var blocked = core.Begin(sessionAvailable: true, recorderAvailable: true, blocker);
            var retry = core.Begin(
                sessionAvailable: true,
                recorderAvailable: true,
                NativeReplayRestartBlocker.None
            );

            Assert.Equal(ManagedReplayRecordingRestartAction.Blocked, blocked.Action);
            Assert.Equal(ManagedReplayRecordingRestartAction.Promote, retry.Action);
        }
    }

    [Fact]
    public void Recorder_blocker_remains_retryable_in_same_session()
    {
        var core = new ManagedReplayRecordingRestartCore();

        var blocked = core.Begin(
            sessionAvailable: true,
            recorderAvailable: false,
            NativeReplayRestartBlocker.None
        );
        var retry = core.Begin(
            sessionAvailable: true,
            recorderAvailable: true,
            NativeReplayRestartBlocker.None
        );

        Assert.Equal(CurrentReplayRecordingStatusCode.RecorderUnavailable, blocked.StatusCode);
        Assert.Equal(ManagedReplayRecordingRestartAction.Promote, retry.Action);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void Promotion_failure_is_terminal(bool operationPromoted, bool publisherPromoted)
    {
        var core = BeginReady();

        var decision = core.OnPromoted(operationPromoted, publisherPromoted);

        AssertTerminal(
            decision,
            CurrentReplayRecordingStatusCode.PromotionFailed,
            ReplayPlaybackReasonCode.RecordingRestartPromotionFailed
        );
        Assert.Equal(ManagedReplayRecordingRestartAction.None, core.OnPromoted(true, true).Action);
    }

    [Fact]
    public void Starting_publish_failure_is_terminal()
    {
        var core = Promote();

        var decision = core.OnStartingPublished(succeeded: false);

        AssertTerminal(
            decision,
            CurrentReplayRecordingStatusCode.StartingPublishFailed,
            ReplayPlaybackReasonCode.RecordingRestartPublishFailed
        );
    }

    [Fact]
    public void Native_invoke_exception_is_terminal()
    {
        var core = PublishStarting();

        var decision = core.OnNativeInvoked(invocationSucceeded: false, replayStarted: false);

        AssertTerminal(
            decision,
            CurrentReplayRecordingStatusCode.NativeInvokeFailed,
            ReplayPlaybackReasonCode.RecordingRestartInvokeFailed
        );
    }

    [Fact]
    public void Native_no_op_is_terminal()
    {
        var core = PublishStarting();

        var decision = core.OnNativeInvoked(invocationSucceeded: true, replayStarted: false);

        AssertTerminal(
            decision,
            CurrentReplayRecordingStatusCode.NativeStartRejected,
            ReplayPlaybackReasonCode.RecordingRestartRejected
        );
    }

    [Fact]
    public void Synchronous_native_transition_completes_started_once()
    {
        var core = PublishStarting();

        var started = core.OnNativeInvoked(invocationSucceeded: true, replayStarted: true);
        var duplicate = core.OnNativeInvoked(invocationSucceeded: true, replayStarted: true);

        Assert.Equal(ManagedReplayRecordingRestartAction.CompleteStarted, started.Action);
        Assert.Equal(ManagedReplayRecordingRestartAction.None, duplicate.Action);
    }

    [Fact]
    public void Failed_session_does_not_contaminate_next_session()
    {
        var failed = PublishStarting();
        Assert.Equal(
            ManagedReplayRecordingRestartAction.CompleteFailed,
            failed.OnNativeInvoked(invocationSucceeded: true, replayStarted: false).Action
        );

        var next = new ManagedReplayRecordingRestartCore();
        Assert.Equal(
            ManagedReplayRecordingRestartAction.Promote,
            next.Begin(
                sessionAvailable: true,
                recorderAvailable: true,
                NativeReplayRestartBlocker.None
            ).Action
        );
    }

    private static ManagedReplayRecordingRestartCore BeginReady()
    {
        var core = new ManagedReplayRecordingRestartCore();
        Assert.Equal(
            ManagedReplayRecordingRestartAction.Promote,
            core.Begin(
                sessionAvailable: true,
                recorderAvailable: true,
                NativeReplayRestartBlocker.None
            ).Action
        );
        return core;
    }

    private static ManagedReplayRecordingRestartCore Promote()
    {
        var core = BeginReady();
        Assert.Equal(
            ManagedReplayRecordingRestartAction.PublishStarting,
            core.OnPromoted(operationPromoted: true, publisherPromoted: true).Action
        );
        return core;
    }

    private static ManagedReplayRecordingRestartCore PublishStarting()
    {
        var core = Promote();
        Assert.Equal(
            ManagedReplayRecordingRestartAction.InvokeNativeReplay,
            core.OnStartingPublished(succeeded: true).Action
        );
        return core;
    }

    private static void AssertTerminal(
        ManagedReplayRecordingRestartDecision decision,
        CurrentReplayRecordingStatusCode expectedStatus,
        ReplayPlaybackReasonCode expectedReason
    )
    {
        Assert.Equal(ManagedReplayRecordingRestartAction.CompleteFailed, decision.Action);
        Assert.Equal(expectedStatus, decision.StatusCode);
        Assert.Equal(expectedReason, decision.FailureReasonCode);
        Assert.False(string.IsNullOrWhiteSpace(decision.EndReason));
    }
}
