#nullable enable
namespace BazaarPlusPlus.GameInterop.CombatReplay;

internal enum NativeReplayRestartBlocker
{
    None,
    ReplayUnavailable,
    ReplayInProgress,
    BoardUnavailable,
    StorageMoving,
    StorageOpen,
    InputBlocked,
    ConnectionLost,
}

internal readonly record struct NativeReplayRestartSnapshot(
    bool ReplayAvailable,
    bool ReplayInProgress,
    bool BoardAvailable,
    bool StorageMoving,
    bool StorageOpen,
    bool InputBlocked,
    bool ConnectionLost
);

internal readonly record struct NativeReplayRestartReadiness(
    bool CanRestart,
    NativeReplayRestartBlocker Blocker
);

/// <summary>
/// Mirrors the native recap replay action's stable interaction gates and adds the BPP requirement
/// that a recording restart begins only after the current playback has finished.
/// </summary>
internal static class NativeReplayRestartGate
{
    internal static NativeReplayRestartReadiness Evaluate(NativeReplayRestartSnapshot snapshot)
    {
        var blocker = snapshot switch
        {
            { ReplayAvailable: false } => NativeReplayRestartBlocker.ReplayUnavailable,
            { ReplayInProgress: true } => NativeReplayRestartBlocker.ReplayInProgress,
            { BoardAvailable: false } => NativeReplayRestartBlocker.BoardUnavailable,
            { StorageMoving: true } => NativeReplayRestartBlocker.StorageMoving,
            { StorageOpen: true } => NativeReplayRestartBlocker.StorageOpen,
            { InputBlocked: true } => NativeReplayRestartBlocker.InputBlocked,
            { ConnectionLost: true } => NativeReplayRestartBlocker.ConnectionLost,
            _ => NativeReplayRestartBlocker.None,
        };

        return new NativeReplayRestartReadiness(
            blocker == NativeReplayRestartBlocker.None,
            blocker
        );
    }
}
