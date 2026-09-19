#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Screenshots;

internal enum ScreenshotCaptureReasonCode
{
    Completed,
    ReadinessDeadline,
    TransitionFieldMissing,
    RevealProbeFailed,
    CleanFrameDeadline,
    NativeTooltipSuppressionUnavailable,
    CleanFrameVisualUnavailable,
    CaptureSynchronousException,
    CaptureTaskFaulted,
    CaptureReturnedNull,
    CaptureArtifactUnavailable,
    ContextExpired,
    CaptureTimeout,
    MetadataUnavailable,
    MetadataFailed,
    MetadataTimeout,
}

internal enum ScreenshotArtifactStatus
{
    Complete,
    FileOnly,
    MetadataPending,
    Unavailable,
}

internal enum ScreenshotCaptureCleanupStage
{
    LateFileDelete,
    RenderTextureRelease,
}

[BppLogEventSource]
internal static class ScreenshotCaptureLogEvents
{
    internal static readonly BppLogFieldDefinition ScreenshotId = PublicField(
        0,
        "screenshot_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition RunId = PublicField(
        1,
        "run_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition CaptureSource = PublicField(
        2,
        "capture_source",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ReasonCode = PublicField(
        3,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ArtifactStatus = PublicField(
        4,
        "artifact_status",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition AttemptCount = PublicField(
        5,
        "attempt_count",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition DurationMs = PublicField(
        6,
        "duration_ms",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition FilePath = new(
        7,
        "file_path",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CaptureSucceeded = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.capture.succeeded",
        [
            ScreenshotId,
            RunId,
            CaptureSource,
            ReasonCode,
            ArtifactStatus,
            AttemptCount,
            DurationMs,
            FilePath,
        ]
    );
    internal static readonly BppLogEventDefinition CaptureDegraded = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.capture.degraded",
        [
            ScreenshotId,
            RunId,
            CaptureSource,
            ReasonCode,
            ArtifactStatus,
            AttemptCount,
            DurationMs,
            FilePath,
        ]
    );
    internal static readonly BppLogEventDefinition CaptureFailed = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.capture.failed",
        [
            ScreenshotId,
            RunId,
            CaptureSource,
            ReasonCode,
            ArtifactStatus,
            AttemptCount,
            DurationMs,
            FilePath,
        ]
    );

    internal static readonly BppLogFieldDefinition CleanupFailedStage = PublicField(
        0,
        "stage",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CleanupFailedScreenshotId = PublicField(
        1,
        "screenshot_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition CleanupFailedFilePath = new(
        2,
        "file_path",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CleanupFailed = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.capture.cleanup_failed",
        [CleanupFailedStage, CleanupFailedScreenshotId, CleanupFailedFilePath]
    );

#if DEBUG
    internal static readonly BppLogFieldDefinition SamplingReadinessCount = PublicField(
        0,
        "readiness_sample_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SamplingReadinessTotalMicroseconds = PublicField(
        1,
        "readiness_total_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SamplingReadinessMaxMicroseconds = PublicField(
        2,
        "readiness_max_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SamplingBarrierCount = PublicField(
        3,
        "barrier_sample_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SamplingBarrierTotalMicroseconds = PublicField(
        4,
        "barrier_total_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SamplingBarrierMaxMicroseconds = PublicField(
        5,
        "barrier_max_us",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SamplingMaxCardCount = PublicField(
        6,
        "max_card_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SamplingMaxTransformCount = PublicField(
        7,
        "max_transform_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SamplingMaxControllerCount = PublicField(
        8,
        "max_controller_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition SamplingMaxSkippedInactiveControllerCount =
        PublicField(9, "max_skipped_inactive_controller_count", BppLogCardinality.High);
    internal static readonly BppLogFieldDefinition SamplingNativeTooltipReasonCode = PublicField(
        10,
        "native_tooltip_reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition SamplingSummary = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.capture.sampling_summary",
        [
            SamplingReadinessCount,
            SamplingReadinessTotalMicroseconds,
            SamplingReadinessMaxMicroseconds,
            SamplingBarrierCount,
            SamplingBarrierTotalMicroseconds,
            SamplingBarrierMaxMicroseconds,
            SamplingMaxCardCount,
            SamplingMaxTransformCount,
            SamplingMaxControllerCount,
            SamplingMaxSkippedInactiveControllerCount,
            SamplingNativeTooltipReasonCode,
        ]
    );
#endif

    private static BppLogFieldDefinition PublicField(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, correlation, cardinality);
}

internal static class ScreenshotCaptureDiagnostics
{
    internal static void ReportCleanupFailed(
        ScreenshotCaptureCleanupStage stage,
        string? screenshotId,
        string? filePath,
        Exception exception
    )
    {
        BppLog.DebugEvent(
            ScreenshotCaptureLogEvents.CleanupFailed,
            exception,
            () =>
                [
                    ScreenshotCaptureLogEvents.CleanupFailedStage.Bind(stage),
                    ScreenshotCaptureLogEvents.CleanupFailedScreenshotId.Bind(screenshotId),
                    ScreenshotCaptureLogEvents.CleanupFailedFilePath.Bind(filePath),
                ]
        );
    }
}
