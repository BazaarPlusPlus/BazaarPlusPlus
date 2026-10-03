#nullable enable

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
