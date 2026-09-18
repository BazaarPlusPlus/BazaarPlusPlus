#nullable enable
using BazaarPlusPlus.GameInterop.CombatReplay;

namespace BazaarPlusPlus.Game.CombatReplay;

internal enum CurrentReplayRecordingStatusCode
{
    None,
    ReplayUnavailable,
    ReplayInProgress,
    BoardUnavailable,
    StorageMoving,
    StorageOpen,
    InputBlocked,
    ConnectionLost,
    RecorderUnavailable,
    SessionChanged,
    PromotionFailed,
    StartingPublishFailed,
    NativeInvokeFailed,
    NativeStartRejected,
    RecordingFailed,
}

internal static class CurrentReplayRecordingStatusCodes
{
    internal static CurrentReplayRecordingStatusCode FromNativeBlocker(
        NativeReplayRestartBlocker blocker
    ) =>
        blocker switch
        {
            NativeReplayRestartBlocker.None => CurrentReplayRecordingStatusCode.None,
            NativeReplayRestartBlocker.ReplayUnavailable =>
                CurrentReplayRecordingStatusCode.ReplayUnavailable,
            NativeReplayRestartBlocker.ReplayInProgress =>
                CurrentReplayRecordingStatusCode.ReplayInProgress,
            NativeReplayRestartBlocker.BoardUnavailable =>
                CurrentReplayRecordingStatusCode.BoardUnavailable,
            NativeReplayRestartBlocker.StorageMoving =>
                CurrentReplayRecordingStatusCode.StorageMoving,
            NativeReplayRestartBlocker.StorageOpen => CurrentReplayRecordingStatusCode.StorageOpen,
            NativeReplayRestartBlocker.InputBlocked =>
                CurrentReplayRecordingStatusCode.InputBlocked,
            NativeReplayRestartBlocker.ConnectionLost =>
                CurrentReplayRecordingStatusCode.ConnectionLost,
            _ => CurrentReplayRecordingStatusCode.ReplayUnavailable,
        };
}
