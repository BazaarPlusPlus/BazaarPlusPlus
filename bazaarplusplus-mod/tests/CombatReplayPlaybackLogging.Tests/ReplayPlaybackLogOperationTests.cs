#nullable enable
using System.Collections.Concurrent;
using BazaarPlusPlus.Game.CombatReplay;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class ReplayPlaybackLogOperationTests
{
    private const string BattleId = "battle-private-12345678";

    [Fact]
    public void Start_is_one_shot_and_preserves_the_operation_context()
    {
        var operation = CreateOperation(recordVideo: true);

        Assert.True(operation.RecordVideo);
        Assert.True(operation.TryMarkStarted(out var started));
        Assert.False(operation.TryMarkStarted(out _));

        Assert.Equal(BattleId, started.BattleId);
        Assert.Equal(CombatReplayPlaybackSource.LocalSaved, started.Source);
        Assert.True(started.RecordVideo);
    }

    [Fact]
    public void Concurrent_start_attempts_produce_one_started_result()
    {
        var operation = CreateOperation();
        Assert.False(operation.RecordVideo);
        var starts = new ConcurrentBag<ReplayPlaybackStartedResult>();

        Parallel.For(
            0,
            64,
            _ =>
            {
                if (operation.TryMarkStarted(out var started))
                    starts.Add(started);
            }
        );

        Assert.Single(starts);
    }

    [Fact]
    public void Ordinary_replay_can_be_promoted_to_recording_before_it_restarts()
    {
        var operation = CreateOperation(recordVideo: false);
        Assert.True(operation.TryMarkStarted(out var initialStart));
        Assert.False(initialStart.RecordVideo);

        Assert.True(operation.TryPromoteToRecording());

        Assert.True(operation.RecordVideo);
        Assert.False(operation.TryMarkStarted(out _));
    }

    [Fact]
    public void Healthy_completion_produces_one_succeeded_terminal()
    {
        var clock = new ManualClock(100);
        var operation = CreateOperation(clock: clock);
        Assert.True(operation.TryMarkStarted(out _));
        clock.Milliseconds = 475;

        Assert.True(
            operation.TryComplete(
                ReplayPlaybackEndReasonCode.StateExit,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.None,
                exception: null,
                out var terminal
            )
        );

        Assert.Equal(ReplayPlaybackTerminalStatus.Succeeded, terminal.Status);
        Assert.Equal(BattleId, terminal.BattleId);
        Assert.Equal(CombatReplayPlaybackSource.LocalSaved, terminal.Source);
        Assert.Equal(ReplayPlaybackEndReasonCode.StateExit, terminal.EndReasonCode);
        Assert.Equal(375, terminal.DurationMilliseconds);
        Assert.Equal(ReplayPlaybackReasonCode.None, terminal.ReasonCode);
        Assert.Equal(0, terminal.DegradationCount);
        Assert.Equal(ReplayRollbackStatus.NotRequired, terminal.RollbackStatus);
        Assert.Null(terminal.Exception);
        Assert.True(operation.IsTerminal);
        Assert.False(
            operation.TryComplete(
                ReplayPlaybackEndReasonCode.RuntimeDestroyed,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.StartException,
                new InvalidOperationException("late completion"),
                out _
            )
        );
    }

    [Fact]
    public void Every_helper_degradation_is_counted_and_the_first_reason_is_primary()
    {
        var firstException = new InvalidOperationException("first helper failure");
        var operation = CreateOperation();
        operation.ReportDegradation(
            ReplayPlaybackReasonCode.PresentationWarmupFailed,
            firstException
        );
        operation.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed);
        operation.ReportDegradation(ReplayPlaybackReasonCode.PresentationWarmupFailed);

        Assert.True(
            operation.TryComplete(
                ReplayPlaybackEndReasonCode.SavedReplayExit,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.None,
                exception: null,
                out var terminal
            )
        );

        Assert.Equal(ReplayPlaybackTerminalStatus.Degraded, terminal.Status);
        Assert.Equal(ReplayPlaybackReasonCode.PresentationWarmupFailed, terminal.ReasonCode);
        Assert.Equal(3, terminal.DegradationCount);
        Assert.Same(firstException, terminal.Exception);
    }

    [Fact]
    public void Explicit_failure_overrides_accumulated_quality_degradation()
    {
        var degradationException = new InvalidOperationException("warmup helper");
        var failureException = new InvalidOperationException("menu return");
        var operation = CreateOperation();
        operation.ReportDegradation(
            ReplayPlaybackReasonCode.CombatVfxWarmupFailed,
            degradationException
        );

        Assert.True(
            operation.TryComplete(
                ReplayPlaybackEndReasonCode.SavedReplayExit,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.MenuReturnFailed,
                failureException,
                out var terminal
            )
        );

        Assert.Equal(ReplayPlaybackTerminalStatus.Failed, terminal.Status);
        Assert.Equal(ReplayPlaybackReasonCode.MenuReturnFailed, terminal.ReasonCode);
        Assert.Equal(1, terminal.DegradationCount);
        Assert.Same(failureException, terminal.Exception);
    }

    [Fact]
    public void Rollback_failure_is_an_explicit_failed_terminal()
    {
        var rollbackException = new InvalidOperationException("rollback");
        var operation = CreateOperation();

        Assert.True(
            operation.TryComplete(
                ReplayPlaybackEndReasonCode.StartFailed,
                ReplayRollbackStatus.Failed,
                ReplayPlaybackReasonCode.None,
                rollbackException,
                out var terminal
            )
        );

        Assert.Equal(ReplayPlaybackTerminalStatus.Failed, terminal.Status);
        Assert.Equal(ReplayPlaybackReasonCode.BootstrapRollbackFailed, terminal.ReasonCode);
        Assert.Equal(ReplayRollbackStatus.Failed, terminal.RollbackStatus);
        Assert.Same(rollbackException, terminal.Exception);
        Assert.False(operation.TryMarkStarted(out _));
    }

    [Fact]
    public void Terminal_failure_is_selected_by_the_explicit_reason_not_exception_presence()
    {
        var operation = CreateOperation();

        Assert.True(
            operation.TryComplete(
                ReplayPlaybackEndReasonCode.StateExit,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.None,
                new InvalidOperationException("non-terminal helper detail"),
                out var terminal
            )
        );

        Assert.Equal(ReplayPlaybackTerminalStatus.Succeeded, terminal.Status);
        Assert.Equal(ReplayPlaybackReasonCode.None, terminal.ReasonCode);
        Assert.Null(terminal.Exception);
    }

    [Fact]
    public void Completion_is_atomic_and_late_degradations_are_ignored()
    {
        var operation = CreateOperation();
        operation.ReportDegradation(ReplayPlaybackReasonCode.AudioWarmupFailed);
        var terminals = new ConcurrentBag<ReplayPlaybackTerminalResult>();

        Parallel.For(
            0,
            64,
            _ =>
            {
                if (
                    operation.TryComplete(
                        ReplayPlaybackEndReasonCode.StateExit,
                        ReplayRollbackStatus.NotRequired,
                        ReplayPlaybackReasonCode.None,
                        exception: null,
                        out var terminal
                    )
                )
                {
                    terminals.Add(terminal);
                }
            }
        );
        operation.ReportDegradation(ReplayPlaybackReasonCode.SoundtrackWarmupFailed);

        var captured = Assert.Single(terminals);
        Assert.Equal(ReplayPlaybackTerminalStatus.Degraded, captured.Status);
        Assert.Equal(1, captured.DegradationCount);
        Assert.Equal(ReplayPlaybackReasonCode.AudioWarmupFailed, captured.ReasonCode);
    }

    [Fact]
    public void Concurrent_start_and_terminal_attempts_never_duplicate_either_result()
    {
        var operation = CreateOperation();
        var starts = new ConcurrentBag<ReplayPlaybackStartedResult>();
        var terminals = new ConcurrentBag<ReplayPlaybackTerminalResult>();

        Parallel.For(
            0,
            128,
            index =>
            {
                if (index % 2 == 0)
                {
                    if (operation.TryMarkStarted(out var started))
                        starts.Add(started);
                    return;
                }

                if (
                    operation.TryComplete(
                        ReplayPlaybackEndReasonCode.StateExit,
                        ReplayRollbackStatus.NotRequired,
                        ReplayPlaybackReasonCode.None,
                        exception: null,
                        out var terminal
                    )
                )
                {
                    terminals.Add(terminal);
                }
            }
        );

        Assert.True(starts.Count <= 1);
        Assert.Single(terminals);
        Assert.False(operation.TryMarkStarted(out _));
        Assert.False(
            operation.TryComplete(
                ReplayPlaybackEndReasonCode.RuntimeDestroyed,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.StartException,
                exception: null,
                out _
            )
        );
    }

    private static ReplayPlaybackLogOperation CreateOperation(
        bool recordVideo = false,
        ManualClock? clock = null
    )
    {
        clock ??= new ManualClock(100);
        return new ReplayPlaybackLogOperation(
            BattleId,
            CombatReplayPlaybackSource.LocalSaved,
            recordVideo,
            clock.Read
        );
    }

    private sealed class ManualClock(long milliseconds)
    {
        internal long Milliseconds { get; set; } = milliseconds;

        internal long Read() => Milliseconds;
    }
}
