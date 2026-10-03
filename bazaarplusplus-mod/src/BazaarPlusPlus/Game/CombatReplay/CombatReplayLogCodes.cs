#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

internal enum ReplayPlaybackReasonCode
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

internal enum ReplayPlaybackEndReasonCode
{
    StateExit,
    SavedReplayExit,
    StartFailed,
    RuntimeDestroyed,
}

internal enum ReplayRollbackStatus
{
    NotRequired,
    Succeeded,
    Failed,
}

internal enum ReplayRequestRejectionReasonCode
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

internal enum ReplayCaptureReasonCode
{
    CaptureOrEnqueueException,
}

internal enum ReplayPersistenceReasonCode
{
    Persisted,
    PersistenceFailed,
    ShutdownAbandoned,
}

internal enum ReplayMaintenanceReasonCode
{
    Completed,
    DeleteFailed,
    ScanFailed,
}

internal enum ReplayWarmupStage
{
    Presentation,
    AudioBanks,
    CombatVfx,
}

internal enum ReplayWarmupAssetReasonCode
{
    AssetLoadFailed,
}

internal enum CurrentReplayPresentationGateOutcome
{
    Ready,
    TimedOut,
}
