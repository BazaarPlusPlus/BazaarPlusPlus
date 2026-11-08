#nullable enable
namespace BazaarPlusPlus.Game.Screenshots;

public enum EndOfRunCaptureReadinessState
{
    UnknownTarget,
    NotSummary,
    TransitionInProgress,
    RevealNotStarted,
    RevealInProgress,
    Ready,
    DetectionFailed,
}

public readonly record struct EndOfRunCaptureReadinessOutcome(
    EndOfRunCaptureReadinessState State,
    ScreenshotCaptureReasonCode? ReasonCode,
    Exception? Exception
);
