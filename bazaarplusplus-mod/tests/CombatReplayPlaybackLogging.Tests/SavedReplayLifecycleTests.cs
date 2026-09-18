#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class SavedReplayLifecycleTests
{
    [Fact]
    public void Mid_start_state_exit_latches_interruption_and_does_not_double_complete()
    {
        var life = new SavedReplayLifecycle();
        life.OnStartBegun();
        life.OnBootstrapResolved(returnToMenuAfter: true);

        var ownership = life.BeginReplayStateExit(now: 10f);
        Assert.True(ownership.OwnsTerminalByStart);

        var decision = life.OnReplayStateExited(now: 10f, publishSucceeded: true, exception: null);
        Assert.Equal(SavedReplayStateExitKind.Defer, decision.Kind);
        Assert.Equal(ReplayPlaybackReasonCode.StartException, decision.LatchReason);

        var interruption = life.TakeStartupInterruption();
        Assert.NotNull(interruption);
        Assert.Equal(ReplayPlaybackReasonCode.StartException, interruption!.Value.ReasonCode);
        Assert.Null(life.TakeStartupInterruption());

        // A second paired Begin+Exited while still in StartFailureCleanup re-owns the terminal
        // (same as the old startCoordinatorOwnsTerminal guard) and must not CompleteNow.
        var secondOwnership = life.BeginReplayStateExit(now: 11f);
        Assert.True(secondOwnership.OwnsTerminalByStart);
        var second = life.OnReplayStateExited(now: 11f, publishSucceeded: true, exception: null);
        Assert.Equal(SavedReplayStateExitKind.Defer, second.Kind);
        // Interruption was already taken; re-latch is fresh.
        Assert.NotNull(life.TakeStartupInterruption());

        life.OnStartFailed();
        Assert.Equal(SavedReplayStartTransition.BecameIdle, life.OnStartFinished());
        Assert.False(life.IsSavedReplayPlaybackActive);
        Assert.False(life.IsReplayStartInProgress);
    }

    [Fact]
    public void Pure_start_failure_without_state_exit_goes_failure_cleanup_then_idle()
    {
        var life = new SavedReplayLifecycle();
        life.OnStartBegun();
        Assert.True(life.IsReplayStartInProgress);
        Assert.True(life.IsSavedReplayPlaybackActive);

        life.OnBootstrapResolved(returnToMenuAfter: true);
        life.OnStartFailed();
        Assert.True(life.IsReplayStartInProgress);
        Assert.False(life.IsSavedReplayPlaybackActive);

        Assert.Equal(SavedReplayStartTransition.BecameIdle, life.OnStartFinished());
        Assert.False(life.IsReplayStartInProgress);
        Assert.False(life.IsSavedReplayPlaybackActive);

        // No latch, no menu-return terminal — pure start failure is completed by the runtime catch.
        Assert.Null(life.TakeStartupInterruption());
        var menu = life.TickMenuReturn(now: 5f, heroSelectLoaded: false);
        Assert.Equal(SavedReplayMenuReturnKind.None, menu.Kind);
    }

    [Fact]
    public void Exit_suppression_blocks_within_window_and_releases_after_including_continue_latch()
    {
        var life = new SavedReplayLifecycle();
        life.OnStartBegun();
        life.OnBootstrapResolved(returnToMenuAfter: false);
        life.OnInjectionCommitted();
        Assert.Equal(SavedReplayStartTransition.BecameActive, life.OnStartFinished());

        // Continue path latches the same suppression owner.
        life.NoteProgrammaticExitLatched(now: 100f);
        Assert.True(life.IsExitSuppressed(now: 100f));
        Assert.True(life.IsExitSuppressed(now: 114.9f));
        Assert.False(life.IsExitSuppressed(now: 115f));

        // Window elapsed: a fresh Exit is allowed through (escape hatch).
        life.ObserveReplayStateGone();
        Assert.False(life.IsExitSuppressed(now: 200f));
    }

    [Fact]
    public void Bootstrapped_exit_suppression_and_repeat_exit_within_window()
    {
        var life = new SavedReplayLifecycle();
        life.OnStartBegun();
        life.OnBootstrapResolved(returnToMenuAfter: true);
        life.OnInjectionCommitted();
        life.OnStartFinished();

        Assert.Equal(
            SavedReplayExitRequestDecision.Proceed,
            life.RequestBootstrappedExit(now: 50f, inReplayState: true)
        );
        Assert.True(life.IsExitSuppressed(now: 50f));
        Assert.False(life.IsSavedReplayPlaybackActive);

        // Duplicate Exit while still in ReplayState reports handled (Suppressed).
        Assert.Equal(
            SavedReplayExitRequestDecision.Suppressed,
            life.RequestBootstrappedExit(now: 51f, inReplayState: true)
        );

        // After the 15s window, suppression lifts even if still "in" ReplayState.
        Assert.Equal(
            SavedReplayExitRequestDecision.NotActive,
            life.RequestBootstrappedExit(now: 65.1f, inReplayState: true)
        );
    }

    [Fact]
    public void Menu_return_confirmed_timeout_and_sync_dispatch_failure()
    {
        var life = new SavedReplayLifecycle();
        life.OnStartBegun();
        life.OnBootstrapResolved(returnToMenuAfter: true);
        life.OnInjectionCommitted();
        life.OnStartFinished();

        // Native state-exit path with bootstrapped flags still set → BeginMenuReturn.
        var ownership = life.BeginReplayStateExit(now: 10f);
        Assert.False(ownership.OwnsTerminalByStart);
        var decision = life.OnReplayStateExited(now: 10f, publishSucceeded: true, exception: null);
        Assert.Equal(SavedReplayStateExitKind.BeginMenuReturn, decision.Kind);
        Assert.Equal(ReplayPlaybackEndReasonCode.StateExit, decision.EndReasonCode);

        Assert.Equal(
            SavedReplayMenuReturnKind.Wait,
            life.TickMenuReturn(now: 10f, heroSelectLoaded: false).Kind
        );

        var confirmed = life.TickMenuReturn(now: 11f, heroSelectLoaded: true);
        Assert.Equal(SavedReplayMenuReturnKind.CompleteConfirmed, confirmed.Kind);
        Assert.Equal(ReplayPlaybackEndReasonCode.StateExit, confirmed.EndReasonCode);
        Assert.Equal(ReplayPlaybackReasonCode.None, confirmed.FailureReason);

        // Timeout path: re-arm via bootstrapped exit.
        life.OnStartBegun();
        life.OnBootstrapResolved(returnToMenuAfter: true);
        life.OnInjectionCommitted();
        life.OnStartFinished();
        Assert.Equal(
            SavedReplayExitRequestDecision.Proceed,
            life.RequestBootstrappedExit(now: 100f, inReplayState: true)
        );
        var armed = life.OnReplayStateExited(now: 100f, publishSucceeded: true, exception: null);
        Assert.Equal(SavedReplayStateExitKind.BeginMenuReturn, armed.Kind);
        Assert.Equal(ReplayPlaybackEndReasonCode.SavedReplayExit, armed.EndReasonCode);

        Assert.Equal(
            SavedReplayMenuReturnKind.Wait,
            life.TickMenuReturn(now: 114.9f, heroSelectLoaded: false).Kind
        );
        var timedOut = life.TickMenuReturn(now: 115f, heroSelectLoaded: false);
        Assert.Equal(SavedReplayMenuReturnKind.CompleteTimeout, timedOut.Kind);
        Assert.Equal(ReplayPlaybackReasonCode.MenuReturnFailed, timedOut.FailureReason);
        Assert.IsType<TimeoutException>(timedOut.Exception);

        // Sync dispatch failure clears pending so no later CompleteTimeout.
        life.OnStartBegun();
        life.OnBootstrapResolved(returnToMenuAfter: true);
        life.OnInjectionCommitted();
        life.OnStartFinished();
        life.BeginReplayStateExit(now: 200f);
        var menu = life.OnReplayStateExited(now: 200f, publishSucceeded: true, exception: null);
        Assert.Equal(SavedReplayStateExitKind.BeginMenuReturn, menu.Kind);
        life.OnMenuReturnDispatchFailed();
        Assert.Equal(
            SavedReplayMenuReturnKind.None,
            life.TickMenuReturn(now: 300f, heroSelectLoaded: false).Kind
        );
    }

    [Fact]
    public void Pending_menu_return_window_allows_a_parallel_new_start()
    {
        var life = new SavedReplayLifecycle();
        life.OnStartBegun();
        life.OnBootstrapResolved(returnToMenuAfter: true);
        life.OnInjectionCommitted();
        life.OnStartFinished();

        life.BeginReplayStateExit(now: 10f);
        var decision = life.OnReplayStateExited(now: 10f, publishSucceeded: true, exception: null);
        Assert.Equal(SavedReplayStateExitKind.BeginMenuReturn, decision.Kind);

        // Pending menu return and start progress are parallel: a new start is allowed.
        Assert.False(life.IsReplayStartInProgress);
        life.OnStartBegun();
        Assert.True(life.IsReplayStartInProgress);
        Assert.True(life.IsSavedReplayPlaybackActive);

        // Pending window still ticks independently of the new start.
        Assert.Equal(
            SavedReplayMenuReturnKind.Wait,
            life.TickMenuReturn(now: 12f, heroSelectLoaded: false).Kind
        );
        var confirmed = life.TickMenuReturn(now: 13f, heroSelectLoaded: true);
        Assert.Equal(SavedReplayMenuReturnKind.CompleteConfirmed, confirmed.Kind);
        Assert.Equal(ReplayPlaybackEndReasonCode.StateExit, confirmed.EndReasonCode);

        // New start can still finish to active after the prior pending completes.
        life.OnBootstrapResolved(returnToMenuAfter: false);
        life.OnInjectionCommitted();
        Assert.Equal(SavedReplayStartTransition.BecameActive, life.OnStartFinished());
        Assert.True(life.IsSavedReplayPlaybackActive);
        Assert.False(life.IsReplayStartInProgress);
    }

    [Fact]
    public void Non_bootstrapped_state_exit_completes_now_and_publish_failure_is_surfaced()
    {
        var life = new SavedReplayLifecycle();
        life.OnStartBegun();
        life.OnBootstrapResolved(returnToMenuAfter: false);
        life.OnInjectionCommitted();
        life.OnStartFinished();

        life.BeginReplayStateExit(now: 1f);
        var ex = new InvalidOperationException("ended publish failed");
        var decision = life.OnReplayStateExited(now: 1f, publishSucceeded: false, exception: ex);
        Assert.Equal(SavedReplayStateExitKind.CompleteNow, decision.Kind);
        Assert.Equal(ReplayPlaybackEndReasonCode.StateExit, decision.EndReasonCode);
        Assert.Equal(ReplayPlaybackReasonCode.EndedPublishFailed, decision.FailureReason);
        Assert.Same(ex, decision.Exception);
    }

    [Fact]
    public void Mid_start_publish_failure_latches_ended_publish_failed()
    {
        var life = new SavedReplayLifecycle();
        life.OnStartBegun();
        life.BeginReplayStateExit(now: 1f);
        var ex = new InvalidOperationException("publish boom");
        var decision = life.OnReplayStateExited(now: 1f, publishSucceeded: false, exception: ex);
        Assert.Equal(SavedReplayStateExitKind.Defer, decision.Kind);
        Assert.Equal(ReplayPlaybackReasonCode.EndedPublishFailed, decision.LatchReason);
        Assert.Same(ex, decision.LatchException);

        var interruption = life.TakeStartupInterruption();
        Assert.NotNull(interruption);
        Assert.Equal(ReplayPlaybackReasonCode.EndedPublishFailed, interruption!.Value.ReasonCode);
        Assert.Same(ex, interruption.Value.Exception);
    }
}
