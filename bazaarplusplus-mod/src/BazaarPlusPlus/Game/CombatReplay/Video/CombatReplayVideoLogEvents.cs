#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

[BppLogEventSource]
internal static class CombatReplayVideoLogEvents
{
    internal static readonly BppLogFieldDefinition RecordingId = Field(
        0,
        "recording_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition BattleId = Field(
        1,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition Source = Field(2, "source");
    internal static readonly BppLogFieldDefinition ReasonCode = Field(3, "reason_code");
    internal static readonly BppLogFieldDefinition DurationMs = Field(
        4,
        "duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition CapturedFrames = Field(
        5,
        "captured_frames",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition DroppedFrames = Field(
        6,
        "dropped_frames",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SizeBytes = Field(
        7,
        "size_bytes",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition AudioStatus = Field(8, "audio_status");
    internal static readonly BppLogFieldDefinition MetadataStatus = Field(9, "metadata_status");
    internal static readonly BppLogFieldDefinition OutputPath = new(
        10,
        "output_path",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition ExitCode = Field(11, "exit_code");
    internal static readonly BppLogFieldDefinition StderrTail = new(
        12,
        "stderr_tail",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );

    internal static readonly BppLogEventDefinition RecordingSucceeded = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.video_recording.succeeded",
        [
            RecordingId,
            BattleId,
            Source,
            ReasonCode,
            DurationMs,
            CapturedFrames,
            DroppedFrames,
            SizeBytes,
            AudioStatus,
            MetadataStatus,
            OutputPath,
        ]
    );
    internal static readonly BppLogEventDefinition RecordingDegraded = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.video_recording.degraded",
        [
            RecordingId,
            BattleId,
            Source,
            ReasonCode,
            DurationMs,
            CapturedFrames,
            DroppedFrames,
            SizeBytes,
            AudioStatus,
            MetadataStatus,
            OutputPath,
        ]
    );
    internal static readonly BppLogEventDefinition RecordingFailed = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.video_recording.failed",
        [
            RecordingId,
            BattleId,
            Source,
            ReasonCode,
            DurationMs,
            CapturedFrames,
            DroppedFrames,
            SizeBytes,
            AudioStatus,
            MetadataStatus,
            OutputPath,
            ExitCode,
            StderrTail,
        ]
    );

    internal static readonly BppLogFieldDefinition AudioStartedRecordingId = Field(
        0,
        "recording_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition AudioStartedBackend = Field(1, "backend");
    internal static readonly BppLogFieldDefinition AudioStartedSampleRate = Field(
        2,
        "sample_rate_hz",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition AudioStartedChannels = Field(3, "channels");
    internal static readonly BppLogFieldDefinition AudioStartedSampleFormat = Field(
        4,
        "sample_format"
    );
    internal static readonly BppLogEventDefinition AudioCaptureStarted = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.audio_capture.started",
        [
            AudioStartedRecordingId,
            AudioStartedBackend,
            AudioStartedSampleRate,
            AudioStartedChannels,
            AudioStartedSampleFormat,
        ]
    );

    internal static readonly BppLogFieldDefinition AudioCompletedRecordingId = Field(
        0,
        "recording_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition AudioCompletedBackend = Field(1, "backend");
    internal static readonly BppLogFieldDefinition AudioCompletedUsable = Field(2, "usable");
    internal static readonly BppLogFieldDefinition AudioCompletedSampleFloatCount = Field(
        3,
        "sample_float_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition AudioCompletedRmsDb = Field(
        4,
        "rms_db",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition AudioCompletedPeakDb = Field(
        5,
        "peak_db",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition AudioCompletedSizeBytes = Field(
        6,
        "size_bytes",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition AudioCompletedWavPath = new(
        7,
        "wav_path",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition AudioCaptureCompleted = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.audio_capture.completed",
        [
            AudioCompletedRecordingId,
            AudioCompletedBackend,
            AudioCompletedUsable,
            AudioCompletedSampleFloatCount,
            AudioCompletedRmsDb,
            AudioCompletedPeakDb,
            AudioCompletedSizeBytes,
            AudioCompletedWavPath,
        ]
    );

    internal static readonly BppLogFieldDefinition LifecycleStage = Field(0, "stage");
    internal static readonly BppLogFieldDefinition LifecycleRecordingId = Field(
        1,
        "recording_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition LifecycleBattleId = Field(
        2,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition LifecyclePendingCount = Field(
        3,
        "pending_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition RecordingLifecycleObserved = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.video_recording.lifecycle_observed",
        [LifecycleStage, LifecycleRecordingId, LifecycleBattleId, LifecyclePendingCount]
    );

    internal static readonly BppLogFieldDefinition StatsRecordingId = Field(
        0,
        "recording_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition StatsStage = Field(1, "stage");
    internal static readonly BppLogFieldDefinition StatsWidth = Field(
        2,
        "width",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsHeight = Field(
        3,
        "height",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsFps = Field(4, "fps");
    internal static readonly BppLogFieldDefinition StatsCapturedFrames = Field(
        5,
        "captured_frames",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsRepeatedFrames = Field(
        6,
        "repeated_frames",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsDroppedFrames = Field(
        7,
        "dropped_frames",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsDurationMs = Field(
        8,
        "duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsSizeBytes = Field(
        9,
        "size_bytes",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsOutputPath = new(
        10,
        "output_path",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsCodec = Field(11, "codec");
    internal static readonly BppLogFieldDefinition StatsRateControl = Field(12, "rate_control");
    internal static readonly BppLogFieldDefinition StatsFrameBytes = Field(
        13,
        "frame_bytes",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsPoolCapacity = Field(14, "pool_capacity");
    internal static readonly BppLogFieldDefinition StatsQueueCapacity = Field(15, "queue_capacity");
    internal static readonly BppLogFieldDefinition StatsPoolPayloadBytes = Field(
        16,
        "pool_payload_bytes",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsPoolBudgetExceeded = Field(
        17,
        "pool_budget_exceeded"
    );
    internal static readonly BppLogFieldDefinition StatsReadbackBackpressureSkips = Field(
        18,
        "readback_backpressure_skips",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsMaxOutstandingReadbacks = Field(
        19,
        "max_outstanding_readbacks"
    );
    internal static readonly BppLogFieldDefinition StatsReadbackCopyP95Us = Field(
        20,
        "readback_copy_p95_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsCfrCopyP95Us = Field(
        21,
        "cfr_copy_p95_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsStagingBufferBytes = Field(
        22,
        "staging_buffer_bytes",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsMaxReadbackPayloadBytes = Field(
        23,
        "max_readback_payload_bytes",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition StatsRenderTextureEstimatedBytes = Field(
        24,
        "render_texture_estimated_bytes",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition VideoCaptureStatsObserved = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.video_capture.stats_observed",
        [
            StatsRecordingId,
            StatsStage,
            StatsWidth,
            StatsHeight,
            StatsFps,
            StatsCapturedFrames,
            StatsRepeatedFrames,
            StatsDroppedFrames,
            StatsDurationMs,
            StatsSizeBytes,
            StatsOutputPath,
            StatsCodec,
            StatsRateControl,
            StatsFrameBytes,
            StatsPoolCapacity,
            StatsQueueCapacity,
            StatsPoolPayloadBytes,
            StatsPoolBudgetExceeded,
            StatsReadbackBackpressureSkips,
            StatsMaxOutstandingReadbacks,
            StatsReadbackCopyP95Us,
            StatsCfrCopyP95Us,
            StatsStagingBufferBytes,
            StatsMaxReadbackPayloadBytes,
            StatsRenderTextureEstimatedBytes,
        ]
    );

    internal static readonly BppLogFieldDefinition NativeStatsRecordingId = Field(
        0,
        "recording_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition NativeStatsStage = Field(1, "stage");
    internal static readonly BppLogFieldDefinition NativeStatsBackpressureDroppedFrames = Field(
        2,
        "backpressure_dropped_frames",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsDroppedFrames = Field(
        3,
        "dropped_frames",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsLeaseMisses = Field(
        4,
        "lease_misses",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsEnqueueRejects = Field(
        5,
        "enqueue_rejects",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsPacerResyncDroppedFrames = Field(
        6,
        "pacer_resync_dropped_frames",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsMaxInFlight = Field(
        7,
        "max_in_flight"
    );
    internal static readonly BppLogFieldDefinition NativeStatsFramesWritten = Field(
        8,
        "native_frames_written",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsRenderFrameP50Us = Field(
        9,
        "render_frame_p50_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsRenderFrameP95Us = Field(
        10,
        "render_frame_p95_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsRenderFrameP99Us = Field(
        11,
        "render_frame_p99_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsTextureCopyP50Us = Field(
        12,
        "texture_copy_p50_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsTextureCopyP95Us = Field(
        13,
        "texture_copy_p95_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsTextureCopyP99Us = Field(
        14,
        "texture_copy_p99_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition NativeStatsBattleId = Field(
        15,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition NativeStatsEncoderName = Field(
        16,
        "encoder_name"
    );
    internal static readonly BppLogEventDefinition VideoCaptureNativePipelineObserved = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.video_capture.native_pipeline_observed",
        [
            NativeStatsRecordingId,
            NativeStatsStage,
            NativeStatsBackpressureDroppedFrames,
            NativeStatsDroppedFrames,
            NativeStatsLeaseMisses,
            NativeStatsEnqueueRejects,
            NativeStatsPacerResyncDroppedFrames,
            NativeStatsMaxInFlight,
            NativeStatsFramesWritten,
            NativeStatsRenderFrameP50Us,
            NativeStatsRenderFrameP95Us,
            NativeStatsRenderFrameP99Us,
            NativeStatsTextureCopyP50Us,
            NativeStatsTextureCopyP95Us,
            NativeStatsTextureCopyP99Us,
            NativeStatsBattleId,
            NativeStatsEncoderName,
        ]
    );

    internal static readonly BppLogFieldDefinition FrameRecordingId = Field(
        0,
        "recording_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition FrameStage = Field(1, "stage");
    internal static readonly BppLogFieldDefinition FrameReasonCode = Field(2, "reason_code");
    internal static readonly BppLogFieldDefinition FrameSequence = Field(
        3,
        "sequence",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition VideoCaptureFrameDegraded = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.video_capture.frame_degraded",
        [FrameRecordingId, FrameStage, FrameReasonCode, FrameSequence]
    );

    internal static readonly BppLogFieldDefinition CleanupRecordingId = Field(
        0,
        "recording_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition CleanupStage = Field(1, "stage");
    internal static readonly BppLogFieldDefinition CleanupPath = new(
        2,
        "path",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition RecordingCleanupFailed = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.video_recording.cleanup_failed",
        [CleanupRecordingId, CleanupStage, CleanupPath]
    );

    internal static readonly BppLogFieldDefinition MuxRecordingId = Field(
        0,
        "recording_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition MuxStage = Field(1, "stage");
    internal static readonly BppLogFieldDefinition MuxReasonCode = Field(2, "reason_code");
    internal static readonly BppLogFieldDefinition MuxPath = new(
        3,
        "path",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition MuxPendingCount = Field(
        4,
        "pending_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition VideoMuxDiagnosticObserved = new(
        BppLogFeatureScope.CombatReplay,
        "combat_replay.video_mux.diagnostic_observed",
        [MuxRecordingId, MuxStage, MuxReasonCode, MuxPath, MuxPendingCount]
    );

    private static BppLogFieldDefinition Field(
        int order,
        string name,
        BppLogCardinality cardinality = BppLogCardinality.Low,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, correlation, cardinality);
}
