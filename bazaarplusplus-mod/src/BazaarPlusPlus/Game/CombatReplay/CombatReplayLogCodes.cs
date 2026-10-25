#nullable enable

namespace BazaarPlusPlus.Game.CombatReplay;

// The desktop-only codes; the playback codes both hosts log are Core's ReplayPlaybackCodes.
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
