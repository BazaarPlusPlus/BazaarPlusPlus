#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

internal enum ReplayCaptureReasonCode
{
    CaptureOrEnqueueException,
    ReplayPartMissing,
}

internal enum ReplayPersistenceReasonCode
{
    Persisted,
    PersistenceFailed,
    ShutdownAbandoned,
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
