#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum ReplayVideoEncoderFailureReasonCode
{
    None,
    WriterTimeout,
    ProcessTimeout,
    NonZeroExit,
    StdinUnavailable,
    StdinWriteFailed,
    StdinCloseFailed,
    WriterCrashed,
}

internal readonly record struct ReplayVideoEncoderCompletionOutcome(
    bool Succeeded,
    ReplayVideoEncoderFailureReasonCode ReasonCode,
    int? ExitCode,
    string StderrTail,
    Exception? Exception
)
{
    internal static ReplayVideoEncoderCompletionOutcome Success(string diagnosticTail) =>
        new(true, ReplayVideoEncoderFailureReasonCode.None, 0, diagnosticTail, null);

    internal static ReplayVideoEncoderCompletionOutcome Failure(
        ReplayVideoEncoderFailureReasonCode reasonCode,
        int? exitCode,
        string diagnosticTail,
        Exception? exception = null
    ) => new(false, reasonCode, exitCode, diagnosticTail, exception);
}

internal enum ReplayVideoLogStage
{
    CaptureStarted,
    CaptureFinalized,
    Readback,
    RenderTextureRelease,
    MuxCallback,
    MuxDrain,
    TempDelete,
    UiSuppression,
    SessionStarted,
}

internal enum ReplayVideoDiagnosticReasonCode
{
    DrainFailed,
}
