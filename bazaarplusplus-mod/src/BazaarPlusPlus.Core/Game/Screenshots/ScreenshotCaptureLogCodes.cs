#nullable enable

namespace BazaarPlusPlus.Game.Screenshots;

public enum ScreenshotCaptureReasonCode
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

public enum ScreenshotArtifactStatus
{
    Complete,
    FileOnly,
    MetadataPending,
    Unavailable,
}

public enum ScreenshotCaptureCleanupStage
{
    LateFileDelete,
    RenderTextureRelease,
}
