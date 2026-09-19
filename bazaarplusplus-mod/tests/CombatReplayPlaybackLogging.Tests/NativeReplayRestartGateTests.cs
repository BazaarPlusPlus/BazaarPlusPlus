using BazaarPlusPlus.GameInterop.CombatReplay;
using Xunit;

namespace CombatReplayPlaybackLogging.Tests;

public sealed class NativeReplayRestartGateTests
{
    [Fact]
    public void Every_native_gate_blocks_before_restart()
    {
        var cases = new (
            NativeReplayRestartSnapshot Snapshot,
            NativeReplayRestartBlocker Expected
        )[]
        {
            (
                Ready() with
                {
                    ReplayAvailable = false,
                },
                NativeReplayRestartBlocker.ReplayUnavailable
            ),
            (Ready() with { ReplayInProgress = true }, NativeReplayRestartBlocker.ReplayInProgress),
            (Ready() with { BoardAvailable = false }, NativeReplayRestartBlocker.BoardUnavailable),
            (Ready() with { StorageMoving = true }, NativeReplayRestartBlocker.StorageMoving),
            (Ready() with { StorageOpen = true }, NativeReplayRestartBlocker.StorageOpen),
            (Ready() with { InputBlocked = true }, NativeReplayRestartBlocker.InputBlocked),
            (Ready() with { ConnectionLost = true }, NativeReplayRestartBlocker.ConnectionLost),
        };

        foreach (var (snapshot, expected) in cases)
        {
            var readiness = NativeReplayRestartGate.Evaluate(snapshot);
            Assert.False(readiness.CanRestart);
            Assert.Equal(expected, readiness.Blocker);
        }
    }

    [Fact]
    public void Ready_snapshot_allows_restart()
    {
        var readiness = NativeReplayRestartGate.Evaluate(Ready());

        Assert.True(readiness.CanRestart);
        Assert.Equal(NativeReplayRestartBlocker.None, readiness.Blocker);
    }

    private static NativeReplayRestartSnapshot Ready() =>
        new(
            ReplayAvailable: true,
            ReplayInProgress: false,
            BoardAvailable: true,
            StorageMoving: false,
            StorageOpen: false,
            InputBlocked: false,
            ConnectionLost: false
        );
}
