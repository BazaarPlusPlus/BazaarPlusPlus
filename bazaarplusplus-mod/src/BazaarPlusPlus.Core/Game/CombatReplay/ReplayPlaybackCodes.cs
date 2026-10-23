#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

// The playback codes both hosts' replay runtimes log; the desktop's capture, persistence,
// warmup and current-replay codes stay in its own CombatReplayLogCodes.
public enum ReplayPlaybackReasonCode
{
    None,
    StartException,
    StartingPublishFailed,
    EndedPublishFailed,
    MenuReturnFailed,
    BootstrapRollbackFailed,
    SocketResolutionFailed,
    SocketCleanupFailed,
    PlayerAttributesUnavailable,
    PlayerSnapshotUnavailable,
    OpponentSnapshotUnavailable,
    PlayerSkillsUnavailable,
    OpponentSkillsUnavailable,
    OpponentIdentityUnavailable,
    OpponentPortraitUnavailable,
    PresentationWarmupFailed,
    AudioWarmupFailed,
    SoundtrackWarmupFailed,
    CombatVfxWarmupFailed,
    RecordingRestartPromotionFailed,
    RecordingRestartPublishFailed,
    RecordingRestartInvokeFailed,
    RecordingRestartRejected,
}

public enum ReplayPlaybackEndReasonCode
{
    StateExit,
    SavedReplayExit,
    StartFailed,
    RuntimeDestroyed,
}

public enum ReplayRollbackStatus
{
    NotRequired,
    Succeeded,
    Failed,
}

public enum ReplayRequestRejectionReasonCode
{
    InvalidBattleId,
    RuntimeUnavailable,
    ReplayAlreadyStarting,
    ActiveRun,
    ReplayAlreadyActive,
    PayloadUnavailable,
    PayloadOperationBusy,
    ManifestUnavailable,
    LoaderUnavailable,
}
