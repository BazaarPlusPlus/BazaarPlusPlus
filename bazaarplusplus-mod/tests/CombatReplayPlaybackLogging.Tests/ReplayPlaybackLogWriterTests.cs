#nullable enable
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Infrastructure;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class ReplayPlaybackLogWriterTests : IDisposable
{
    public ReplayPlaybackLogWriterTests() => BppLog.Reset();

    public void Dispose() => BppLog.Reset();

    [Fact]
    public void Started_record_maps_to_the_one_structured_Info_event()
    {
        ReplayPlaybackLogWriter.EmitStarted(
            new ReplayPlaybackStartedResult(
                "battle-private-12345678",
                CombatReplayPlaybackSource.ImportedGhost,
                RecordVideo: true
            )
        );

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal("Info", captured.Severity);
        Assert.Equal("combat_replay.playback.started", captured.Definition.EventId);
        AssertFields(
            captured,
            ("battle_id", "battle-private-12345678"),
            ("source", CombatReplayPlaybackSource.ImportedGhost),
            ("record_video", true)
        );
    }

    [Theory]
    [InlineData(
        (int)ReplayPlaybackTerminalStatus.Succeeded,
        "Info",
        "combat_replay.playback.succeeded"
    )]
    [InlineData(
        (int)ReplayPlaybackTerminalStatus.Degraded,
        "Warning",
        "combat_replay.playback.degraded"
    )]
    [InlineData((int)ReplayPlaybackTerminalStatus.Failed, "Error", "combat_replay.playback.failed")]
    public void Terminal_status_maps_to_exactly_one_authoritative_event(
        int statusValue,
        string severity,
        string eventId
    )
    {
        var status = (ReplayPlaybackTerminalStatus)statusValue;
        var exception =
            status == ReplayPlaybackTerminalStatus.Succeeded
                ? null
                : new InvalidOperationException("terminal detail");

        ReplayPlaybackLogWriter.EmitTerminal(
            new ReplayPlaybackTerminalResult(
                status,
                "battle-private-12345678",
                CombatReplayPlaybackSource.LocalSaved,
                ReplayPlaybackEndReasonCode.StateExit,
                DurationMilliseconds: 425,
                status == ReplayPlaybackTerminalStatus.Succeeded
                    ? ReplayPlaybackReasonCode.None
                    : ReplayPlaybackReasonCode.AudioWarmupFailed,
                DegradationCount: status == ReplayPlaybackTerminalStatus.Succeeded ? 0 : 2,
                ReplayRollbackStatus.NotRequired,
                exception
            )
        );

        var captured = Assert.Single(BppLog.Events);
        Assert.Equal(severity, captured.Severity);
        Assert.Equal(eventId, captured.Definition.EventId);
        Assert.Same(exception, captured.Exception);
        AssertFields(
            captured,
            ("battle_id", "battle-private-12345678"),
            ("source", CombatReplayPlaybackSource.LocalSaved),
            ("end_reason_code", ReplayPlaybackEndReasonCode.StateExit),
            ("duration_ms", 425L),
            (
                "reason_code",
                status == ReplayPlaybackTerminalStatus.Succeeded
                    ? ReplayPlaybackReasonCode.None
                    : ReplayPlaybackReasonCode.AudioWarmupFailed
            ),
            ("degradation_count", status == ReplayPlaybackTerminalStatus.Succeeded ? 0 : 2),
            ("rollback_status", ReplayRollbackStatus.NotRequired)
        );
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
