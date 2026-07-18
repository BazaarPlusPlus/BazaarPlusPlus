#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

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
    OpponentPortraitCleanupFailed,
    PresentationWarmupFailed,
    AudioWarmupFailed,
    SoundtrackWarmupFailed,
    CombatVfxWarmupFailed,
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
    ManifestUnavailable,
    LoaderUnavailable,
}

internal enum ReplayCaptureReasonCode
{
    CaptureOrEnqueueException,
}

internal enum ReplayExternalRecordSource
{
    Agent,
}

internal enum ReplayPersistenceReasonCode
{
    Persisted,
    PersistenceFailed,
    ShutdownAbandoned,
    OrphanDeleteFailed,
    OrphanScanFailed,
}

internal enum ReplayWarmupStage
{
    Presentation,
    AudioBanks,
    Soundtrack,
    CombatVfx,
}

internal enum ReplayWarmupAssetReasonCode
{
    AssetUnavailable,
    AssetLoadFailed,
    InvalidAssetKey,
}

[BppLogEventSource]
internal static class CombatReplayLogEvents
{
    internal static readonly BppLogFieldDefinition CaptureFailedRunId = Public(
        0,
        "run_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition CaptureFailedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CaptureFailed = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.capture.failed",
        [CaptureFailedRunId, CaptureFailedReasonCode]
    );

    internal static readonly BppLogFieldDefinition CurrentRecordingUiPhase = Public(
        0,
        "phase",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CurrentRecordingUiSnapshotVisible = Public(
        1,
        "snapshot_visible",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CurrentRecordingUiLayoutAvailable = Public(
        2,
        "layout_available",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CurrentRecordingUiLayoutReasonCode = Public(
        3,
        "layout_reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CurrentRecordingUiCloneActive = Public(
        4,
        "clone_active",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CurrentRecordingUiNativeReplayBound = Public(
        5,
        "native_replay_bound",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CurrentRecordingUiIconAvailable = Public(
        6,
        "icon_available",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CurrentRecordingUiObserved = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.current_recording_ui.observed",
        [
            CurrentRecordingUiPhase,
            CurrentRecordingUiSnapshotVisible,
            CurrentRecordingUiLayoutAvailable,
            CurrentRecordingUiLayoutReasonCode,
            CurrentRecordingUiCloneActive,
            CurrentRecordingUiNativeReplayBound,
            CurrentRecordingUiIconAvailable,
        ]
    );

    internal static readonly BppLogFieldDefinition RequestRejectedSource = Public(
        0,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition RequestRejectedReasonCode = Public(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition RequestRejectedBattleId = Public(
        2,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogEventDefinition RequestRejected = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.playback.request_rejected",
        [RequestRejectedSource, RequestRejectedReasonCode, RequestRejectedBattleId]
    );

    internal static readonly BppLogFieldDefinition ExternalRecordAcceptedRequestId = Public(
        0,
        "request_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition ExternalRecordAcceptedBattleId = Public(
        1,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition ExternalRecordAcceptedSource = Public(
        2,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition ExternalRecordAccepted = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.external_record.accepted",
        [
            ExternalRecordAcceptedRequestId,
            ExternalRecordAcceptedBattleId,
            ExternalRecordAcceptedSource,
        ]
    );

    internal static readonly BppLogFieldDefinition PlaybackStartedBattleId = Public(
        0,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition PlaybackStartedSource = Public(
        1,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition PlaybackStartedRecordVideo = Public(
        2,
        "record_video",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition PlaybackStarted = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.playback.started",
        [PlaybackStartedBattleId, PlaybackStartedSource, PlaybackStartedRecordVideo]
    );

    internal static readonly BppLogFieldDefinition PlaybackTerminalBattleId = Public(
        0,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition PlaybackTerminalSource = Public(
        1,
        "source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition PlaybackTerminalEndReasonCode = Public(
        2,
        "end_reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition PlaybackTerminalDurationMs = Public(
        3,
        "duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition PlaybackTerminalReasonCode = Public(
        4,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition PlaybackTerminalDegradationCount = Public(
        5,
        "degradation_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition PlaybackTerminalRollbackStatus = Public(
        6,
        "rollback_status",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition PlaybackSucceeded = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.playback.succeeded",
        [
            PlaybackTerminalBattleId,
            PlaybackTerminalSource,
            PlaybackTerminalEndReasonCode,
            PlaybackTerminalDurationMs,
            PlaybackTerminalReasonCode,
            PlaybackTerminalDegradationCount,
            PlaybackTerminalRollbackStatus,
        ]
    );
    internal static readonly BppLogEventDefinition PlaybackDegraded = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.playback.degraded",
        [
            PlaybackTerminalBattleId,
            PlaybackTerminalSource,
            PlaybackTerminalEndReasonCode,
            PlaybackTerminalDurationMs,
            PlaybackTerminalReasonCode,
            PlaybackTerminalDegradationCount,
            PlaybackTerminalRollbackStatus,
        ]
    );
    internal static readonly BppLogEventDefinition PlaybackFailed = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.playback.failed",
        [
            PlaybackTerminalBattleId,
            PlaybackTerminalSource,
            PlaybackTerminalEndReasonCode,
            PlaybackTerminalDurationMs,
            PlaybackTerminalReasonCode,
            PlaybackTerminalDegradationCount,
            PlaybackTerminalRollbackStatus,
        ]
    );

    internal static readonly BppLogFieldDefinition PersistenceBattleId = Public(
        0,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition PersistenceRunId = Public(
        1,
        "run_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition PersistenceReasonCode = Public(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition PersistenceFailed = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.persistence.failed",
        [PersistenceBattleId, PersistenceRunId, PersistenceReasonCode]
    );
    internal static readonly BppLogEventDefinition PersistenceSucceeded = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.persistence.succeeded",
        [PersistenceBattleId, PersistenceRunId, PersistenceReasonCode]
    );

    internal static readonly BppLogFieldDefinition OrphanCleanupReasonCode = Public(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition OrphanCleanupFailedCount = Public(
        1,
        "failed_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition OrphanCleanupDegraded = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.persistence.orphan_cleanup_degraded",
        [OrphanCleanupReasonCode, OrphanCleanupFailedCount],
        new BppLogStormPolicy([OrphanCleanupReasonCode])
    );

    internal static readonly BppLogFieldDefinition ShutdownPendingCount = Public(
        0,
        "pending_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition ShutdownInFlight = Public(
        1,
        "in_flight",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ShutdownTimeoutMs = Public(
        2,
        "timeout_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition PersistenceShutdownIncomplete = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.persistence.shutdown_incomplete",
        [ShutdownPendingCount, ShutdownInFlight, ShutdownTimeoutMs]
    );

    internal static readonly BppLogFieldDefinition RollbackCleanupBattleId = Public(
        0,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogEventDefinition PersistenceRollbackCleanupFailed = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.persistence.rollback_cleanup_failed",
        [RollbackCleanupBattleId]
    );

    internal static readonly BppLogFieldDefinition CleanupObservedStage = Public(
        0,
        "stage",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CleanupObservedRemovedCount = Public(
        1,
        "removed_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CleanupObservedBattleId = Public(
        2,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogEventDefinition PlaybackCleanupObserved = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.playback.cleanup_observed",
        [CleanupObservedStage, CleanupObservedRemovedCount, CleanupObservedBattleId]
    );

    internal static readonly BppLogFieldDefinition WarmupCompletedStage = Public(
        0,
        "stage",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition WarmupCompletedBattleId = Public(
        1,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition WarmupCompletedDurationMs = Public(
        2,
        "duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupBoardBankLoadedCount = Public(
        3,
        "board_bank_loaded_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupBoardBankAlreadyLoadedCount = Public(
        4,
        "board_bank_already_loaded_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupBoardBankFailedCount = Public(
        5,
        "board_bank_failed_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupBoardBankSkippedCount = Public(
        6,
        "board_bank_skipped_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupSoundtrackBankLoadedCount = Public(
        7,
        "soundtrack_bank_loaded_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupSoundtrackBankAlreadyLoadedCount = Public(
        8,
        "soundtrack_bank_already_loaded_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupSoundtrackBankFailedCount = Public(
        9,
        "soundtrack_bank_failed_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupSoundtrackBankSkippedCount = Public(
        10,
        "soundtrack_bank_skipped_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupSharedAssetPreloadedCount = Public(
        11,
        "shared_asset_preloaded_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupSharedAssetSkippedCount = Public(
        12,
        "shared_asset_skipped_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupCardPreloadedCount = Public(
        13,
        "card_preloaded_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupCardSkippedCount = Public(
        14,
        "card_skipped_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupCardFailedCount = Public(
        15,
        "card_failed_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupOverrideAssetPreloadedCount = Public(
        16,
        "override_asset_preloaded_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupOverrideAssetSkippedCount = Public(
        17,
        "override_asset_skipped_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupOverrideAssetFailedCount = Public(
        18,
        "override_asset_failed_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupVfxPrewarmedCount = Public(
        19,
        "vfx_prewarmed_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupVfxSkippedCount = Public(
        20,
        "vfx_skipped_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupVfxFailedCount = Public(
        21,
        "vfx_failed_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition WarmupCompleted = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.warmup.completed",
        [
            WarmupCompletedStage,
            WarmupCompletedBattleId,
            WarmupCompletedDurationMs,
            WarmupBoardBankLoadedCount,
            WarmupBoardBankAlreadyLoadedCount,
            WarmupBoardBankFailedCount,
            WarmupBoardBankSkippedCount,
            WarmupSoundtrackBankLoadedCount,
            WarmupSoundtrackBankAlreadyLoadedCount,
            WarmupSoundtrackBankFailedCount,
            WarmupSoundtrackBankSkippedCount,
            WarmupSharedAssetPreloadedCount,
            WarmupSharedAssetSkippedCount,
            WarmupCardPreloadedCount,
            WarmupCardSkippedCount,
            WarmupCardFailedCount,
            WarmupOverrideAssetPreloadedCount,
            WarmupOverrideAssetSkippedCount,
            WarmupOverrideAssetFailedCount,
            WarmupVfxPrewarmedCount,
            WarmupVfxSkippedCount,
            WarmupVfxFailedCount,
        ]
    );

    internal static readonly BppLogFieldDefinition WarmupAssetStage = Public(
        0,
        "stage",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition WarmupAssetKey = new(
        1,
        "asset_key",
        BppLogFieldPrivacy.UntrustedText,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WarmupAssetReasonCode = Public(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition WarmupAssetSkipped = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.warmup.asset_skipped",
        [WarmupAssetStage, WarmupAssetKey, WarmupAssetReasonCode]
    );

    private static BppLogFieldDefinition Public(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, BppLogFieldPrivacy.Public, correlation, cardinality);
}
