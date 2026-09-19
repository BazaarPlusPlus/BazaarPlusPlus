#nullable enable
using BazaarPlusPlus.Game.HistoryPanel;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Xunit;

namespace HistoryPanelOperationalLogging.Tests;

public sealed class HistoryPanelLogWriterTests : IDisposable
{
    public HistoryPanelLogWriterTests() => BppLog.Reset();

    public void Dispose() => BppLog.Reset();

    [Fact]
    public void Replay_failure_maps_to_one_Error_without_display_prose()
    {
        var exception = new InvalidOperationException("safe exception");

        HistoryPanelLogWriter.EmitReplayFailed(
            new HistoryPanelReplayFailedResult(
                "request-private",
                "battle-private",
                true,
                HistoryPanelReplayReasonCode.GhostArtifactInvalid,
                exception
            )
        );

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal("Error", captured.Severity);
        Assert.Equal("history_panel.replay.failed", captured.Definition.EventId);
        Assert.Same(exception, captured.Exception);
        AssertFields(
            captured,
            ("request_id", "request-private"),
            ("battle_id", "battle-private"),
            ("record_video", true),
            ("reason_code", HistoryPanelReplayReasonCode.GhostArtifactInvalid)
        );
    }

    [Theory]
    [InlineData(
        (int)HistoryPanelRunDeleteTerminalStatus.Failed,
        "Error",
        "history_panel.run_delete.failed"
    )]
    [InlineData(
        (int)HistoryPanelRunDeleteTerminalStatus.Degraded,
        "Warning",
        "history_panel.run_delete.degraded"
    )]
    [InlineData(
        (int)HistoryPanelRunDeleteTerminalStatus.Succeeded,
        "Info",
        "history_panel.run_delete.succeeded"
    )]
    public void Delete_terminal_maps_to_one_authoritative_event(
        int statusValue,
        string severity,
        string eventId
    )
    {
        var status = (HistoryPanelRunDeleteTerminalStatus)statusValue;
        HistoryPanelLogWriter.EmitRunDeleteTerminal(
            new HistoryPanelRunDeleteTerminalResult(
                status,
                "request-private",
                "run-private",
                7,
                status == HistoryPanelRunDeleteTerminalStatus.Degraded ? 2 : 0,
                status == HistoryPanelRunDeleteTerminalStatus.Succeeded
                        ? HistoryPanelRunDeleteReasonCode.Completed
                    : status == HistoryPanelRunDeleteTerminalStatus.Degraded
                        ? HistoryPanelRunDeleteReasonCode.ReplayPayloadCleanupFailed
                    : HistoryPanelRunDeleteReasonCode.PrimaryDeleteFailed,
                Exception: null
            )
        );

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal(severity, captured.Severity);
        Assert.Equal(eventId, captured.Definition.EventId);
        AssertFields(
            captured,
            ("request_id", "request-private"),
            ("run_id", "run-private"),
            ("battle_count", 7),
            (
                "cleanup_failed_count",
                status == HistoryPanelRunDeleteTerminalStatus.Degraded ? 2 : 0
            ),
            (
                "reason_code",
                status == HistoryPanelRunDeleteTerminalStatus.Succeeded
                    ? HistoryPanelRunDeleteReasonCode.Completed
                : status == HistoryPanelRunDeleteTerminalStatus.Degraded
                    ? HistoryPanelRunDeleteReasonCode.ReplayPayloadCleanupFailed
                : HistoryPanelRunDeleteReasonCode.PrimaryDeleteFailed
            )
        );
    }

    [Fact]
    public void Health_returned_error_text_never_enters_the_terminal_event()
    {
        const string privateError = "token=token-private response_body=body-private";
        HistoryPanelLogWriter.EmitServerHealthTerminal(
            new HistoryPanelServerHealthTerminalResult(
                HistoryPanelServerHealthTerminalStatus.Failed,
                "request-private",
                145,
                HistoryPanelServerHealthReasonClassifier.Classify(privateError),
                Exception: null
            )
        );

        var captured = Assert.Single(BppLog.Events);
        var rendered = new BppLogEventRenderer().Render(
            captured.Definition,
            captured.Values,
            captured.Exception
        );
        Assert.Equal("Error", captured.Severity);
        Assert.DoesNotContain(privateError, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("token-private", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("body-private", rendered, StringComparison.Ordinal);
        AssertFields(
            captured,
            ("request_id", "request-private"),
            ("duration_ms", 145L),
            ("reason_code", HistoryPanelServerHealthReasonCode.TransportFailure)
        );
    }

    [Theory]
    [InlineData(
        (int)HistoryPanelGhostSyncTerminalStatus.Failed,
        "Error",
        "history_panel.ghost_sync.failed"
    )]
    [InlineData(
        (int)HistoryPanelGhostSyncTerminalStatus.Succeeded,
        "Info",
        "history_panel.ghost_sync.succeeded"
    )]
    public void Sync_terminal_maps_to_one_event(int statusValue, string severity, string eventId)
    {
        var status = (HistoryPanelGhostSyncTerminalStatus)statusValue;
        HistoryPanelLogWriter.EmitGhostSyncTerminal(
            new HistoryPanelGhostSyncTerminalResult(
                status,
                "request-private",
                status == HistoryPanelGhostSyncTerminalStatus.Succeeded ? 19 : 0,
                status == HistoryPanelGhostSyncTerminalStatus.Succeeded
                    ? HistoryPanelGhostSyncReasonCode.Completed
                    : HistoryPanelGhostSyncReasonCode.QueryFailed,
                Exception: null
            )
        );

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal(severity, captured.Severity);
        Assert.Equal(eventId, captured.Definition.EventId);
    }

    private static void AssertFields(
        CapturedBppLogEvent captured,
        params (string Name, object? Value)[] expected
    )
    {
        Assert.Equal(expected.Length, captured.Values.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Name, captured.Values[index].Field.Name);
            Assert.Equal(expected[index].Value, captured.Values[index].Value);
        }
    }
}
