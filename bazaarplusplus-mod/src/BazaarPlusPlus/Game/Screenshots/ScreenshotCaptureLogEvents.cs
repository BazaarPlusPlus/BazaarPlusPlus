#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Screenshots;

internal enum ScreenshotCaptureReasonCode
{
    Completed,
    OutputPathUnavailable,
    ReadinessDeadline,
    TransitionFieldMissing,
    RevealProbeFailed,
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
    internal static readonly BppLogFieldDefinition InitializationFailedReasonCode = PublicField(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition InitializationFailed = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.capture.initialization_failed",
        [InitializationFailedReasonCode]
    );

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
        BppLogFieldPrivacy.LocalPath,
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
        BppLogFieldPrivacy.LocalPath,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition CleanupFailed = new(
        BppLogFeatureScope.Screenshots,
        "screenshots.capture.cleanup_failed",
        [CleanupFailedStage, CleanupFailedScreenshotId, CleanupFailedFilePath]
    );

    private static BppLogFieldDefinition PublicField(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, BppLogFieldPrivacy.Public, correlation, cardinality);
}

internal static class ScreenshotCaptureDiagnostics
{
    internal static void ReportInitializationFailed(Exception? exception = null)
    {
        var field = ScreenshotCaptureLogEvents.InitializationFailedReasonCode.Bind(
            ScreenshotCaptureReasonCode.OutputPathUnavailable
        );
        if (exception == null)
            BppLog.ErrorEvent(ScreenshotCaptureLogEvents.InitializationFailed, field);
        else
            BppLog.ErrorEvent(ScreenshotCaptureLogEvents.InitializationFailed, exception, field);
    }

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
