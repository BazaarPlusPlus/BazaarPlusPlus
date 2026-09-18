#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using Xunit;

namespace HistoryPanelOperationalLogging.Tests;

public sealed class HistoryPanelLogOperationTests
{
    [Fact]
    public void Replay_current_cancellation_fails_once_while_stale_cancellation_abandons_silently()
    {
        var current = new HistoryPanelReplayLogOperation(
            "request-current",
            "battle-current",
            false
        );
        var stale = new HistoryPanelReplayLogOperation("request-stale", "battle-stale", false);

        Assert.Equal(
            HistoryPanelCancellationDisposition.FailCurrentRequest,
            HistoryPanelCancellationRouter.Resolve(isCurrentSession: true)
        );
        Assert.True(
            current.TryFail(
                HistoryPanelReplayReasonCode.Canceled,
                new OperationCanceledException(),
                out var failed
            )
        );
        Assert.Equal(HistoryPanelReplayReasonCode.Canceled, failed.ReasonCode);

        Assert.Equal(
            HistoryPanelCancellationDisposition.AbandonStaleRequest,
            HistoryPanelCancellationRouter.Resolve(isCurrentSession: false)
        );
        stale.Abandon();
        Assert.False(stale.TryFail(HistoryPanelReplayReasonCode.Canceled, exception: null, out _));
    }

    [Fact]
    public void Health_current_cancellation_fails_once_while_stale_cancellation_abandons_silently()
    {
        var current = new HistoryPanelServerHealthLogOperation("request-current", () => 1);
        var stale = new HistoryPanelServerHealthLogOperation("request-stale", () => 1);

        Assert.Equal(
            HistoryPanelCancellationDisposition.FailCurrentRequest,
            HistoryPanelCancellationRouter.Resolve(isCurrentSession: true)
        );
        Assert.True(
            current.TryComplete(
                HistoryPanelServerHealthTerminalStatus.Failed,
                HistoryPanelServerHealthReasonCode.Canceled,
                new OperationCanceledException(),
                out var failed
            )
        );
        Assert.Equal(HistoryPanelServerHealthReasonCode.Canceled, failed.ReasonCode);

        Assert.Equal(
            HistoryPanelCancellationDisposition.AbandonStaleRequest,
            HistoryPanelCancellationRouter.Resolve(isCurrentSession: false)
        );
        stale.Abandon();
        Assert.False(
            stale.TryComplete(
                HistoryPanelServerHealthTerminalStatus.Failed,
                HistoryPanelServerHealthReasonCode.Canceled,
                exception: null,
                out _
            )
        );
    }

    [Fact]
    public void Sync_current_cancellation_fails_once_while_stale_cancellation_abandons_silently()
    {
        var current = new HistoryPanelGhostSyncLogOperation("request-current");
        var stale = new HistoryPanelGhostSyncLogOperation("request-stale");

        Assert.Equal(
            HistoryPanelCancellationDisposition.FailCurrentRequest,
            HistoryPanelCancellationRouter.Resolve(isCurrentSession: true)
        );
        Assert.True(
            current.TryFail(
                HistoryPanelGhostSyncReasonCode.Canceled,
                new OperationCanceledException(),
                out var failed
            )
        );
        Assert.Equal(HistoryPanelGhostSyncReasonCode.Canceled, failed.ReasonCode);

        Assert.Equal(
            HistoryPanelCancellationDisposition.AbandonStaleRequest,
            HistoryPanelCancellationRouter.Resolve(isCurrentSession: false)
        );
        stale.Abandon();
        Assert.False(
            stale.TryFail(HistoryPanelGhostSyncReasonCode.Canceled, exception: null, out _)
        );
    }

    [Fact]
    public void Replay_preflight_is_nonterminal_and_acceptance_is_one_shot()
    {
        var operation = new HistoryPanelReplayLogOperation(
            "request-private",
            "battle-private",
            true
        );

        Assert.True(
            operation.TryRecordPreflight(
                true,
                HistoryPanelReplayReasonCode.RecordingAvailable,
                out var preflight
            )
        );
        Assert.False(
            operation.TryRecordPreflight(
                false,
                HistoryPanelReplayReasonCode.RecordingUnavailable,
                out _
            )
        );
        Assert.True(operation.TryAccept(out var accepted));
        Assert.False(
            operation.TryFail(
                HistoryPanelReplayReasonCode.UnexpectedException,
                new InvalidOperationException("late"),
                out _
            )
        );

        Assert.Equal("request-private", preflight.RequestId);
        Assert.True(preflight.CanRecord);
        Assert.Equal("battle-private", accepted.BattleId);
        Assert.True(accepted.RecordVideo);
    }

    [Fact]
    public void Replay_abandon_is_silent_and_terminal()
    {
        var operation = new HistoryPanelReplayLogOperation(
            "request-private",
            "battle-private",
            false
        );

        operation.Abandon();

        Assert.False(operation.TryAccept(out _));
        Assert.False(
            operation.TryFail(HistoryPanelReplayReasonCode.Canceled, exception: null, out _)
        );
    }

    [Theory]
    [InlineData((int)HistoryPanelRunDeleteTerminalStatus.Failed)]
    [InlineData((int)HistoryPanelRunDeleteTerminalStatus.Degraded)]
    [InlineData((int)HistoryPanelRunDeleteTerminalStatus.Succeeded)]
    public void Delete_request_has_exactly_one_terminal(int statusValue)
    {
        var operation = new HistoryPanelRunDeleteLogOperation("request-private", "run-private");
        var status = (HistoryPanelRunDeleteTerminalStatus)statusValue;

        Assert.True(
            operation.TryComplete(
                status,
                battleCount: 5,
                cleanupFailedCount: status == HistoryPanelRunDeleteTerminalStatus.Degraded ? 2 : 0,
                status switch
                {
                    HistoryPanelRunDeleteTerminalStatus.Failed =>
                        HistoryPanelRunDeleteReasonCode.PrimaryDeleteFailed,
                    HistoryPanelRunDeleteTerminalStatus.Degraded =>
                        HistoryPanelRunDeleteReasonCode.ReplayPayloadCleanupFailed,
                    _ => HistoryPanelRunDeleteReasonCode.Completed,
                },
                exception: null,
                out var result
            )
        );
        Assert.False(
            operation.TryComplete(
                HistoryPanelRunDeleteTerminalStatus.Failed,
                0,
                0,
                HistoryPanelRunDeleteReasonCode.PrimaryDeleteFailed,
                null,
                out _
            )
        );
        Assert.Equal(status, result.Status);
        Assert.Equal(5, result.BattleCount);
    }

    [Fact]
    public void Health_duration_is_monotonic_nonnegative_and_terminal()
    {
        long now = 100;
        var operation = new HistoryPanelServerHealthLogOperation("request-private", () => now);
        now = 425;

        Assert.True(
            operation.TryComplete(
                HistoryPanelServerHealthTerminalStatus.Succeeded,
                HistoryPanelServerHealthReasonCode.Completed,
                exception: null,
                out var result
            )
        );
        Assert.Equal(325, result.DurationMilliseconds);
        now = 50;
        Assert.False(
            operation.TryComplete(
                HistoryPanelServerHealthTerminalStatus.Failed,
                HistoryPanelServerHealthReasonCode.TransportFailure,
                null,
                out _
            )
        );
    }

    [Fact]
    public void Sync_success_preserves_imported_count_and_blocks_late_failure()
    {
        var operation = new HistoryPanelGhostSyncLogOperation("request-private");

        Assert.True(operation.TrySucceed(17, out var result));
        Assert.False(
            operation.TryFail(HistoryPanelGhostSyncReasonCode.QueryFailed, exception: null, out _)
        );
        Assert.Equal(17, result.ImportedCount);
        Assert.Equal(HistoryPanelGhostSyncReasonCode.Completed, result.ReasonCode);
    }

    [Theory]
    [InlineData("http_503", (int)HistoryPanelServerHealthReasonCode.HttpFailure)]
    [InlineData("health_status_not_ok", (int)HistoryPanelServerHealthReasonCode.HealthStatusNotOk)]
    [InlineData("server_time_invalid", (int)HistoryPanelServerHealthReasonCode.ServerTimeInvalid)]
    [InlineData(
        "token=private response_body=private",
        (int)HistoryPanelServerHealthReasonCode.TransportFailure
    )]
    public void Health_failure_text_is_reduced_to_a_closed_reason(string error, int expectedValue)
    {
        Assert.Equal(
            (HistoryPanelServerHealthReasonCode)expectedValue,
            HistoryPanelServerHealthReasonClassifier.Classify(error)
        );
    }
}
