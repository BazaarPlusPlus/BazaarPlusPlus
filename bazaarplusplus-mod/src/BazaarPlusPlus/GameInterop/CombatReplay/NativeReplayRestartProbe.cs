#nullable enable
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.CombatReplay;

internal static class NativeReplayRestartProbe
{
    internal static NativeReplayRestartReadiness Observe(ReplayState? replay)
    {
        var boardManager = Singleton<BoardManager>.Instance;
        return NativeReplayRestartGate.Evaluate(
            new NativeReplayRestartSnapshot(
                ReplayAvailable: replay != null,
                ReplayInProgress: replay?.IsReplaying == true,
                BoardAvailable: boardManager != null,
                StorageMoving: boardManager?.StorageMoving == true,
                StorageOpen: Data.IsStorageOpen,
                InputBlocked: AppState.BlockInput,
                ConnectionLost: boardManager?.SocketConnectionLost == true
            )
        );
    }
}
