#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum FfmpegEncoderFailureReasonCode
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

internal readonly record struct FfmpegEncoderCompletionOutcome(
    bool Succeeded,
    FfmpegEncoderFailureReasonCode ReasonCode,
    int? ExitCode,
    string StderrTail,
    Exception? Exception
)
{
    internal static FfmpegEncoderCompletionOutcome Success(string stderrTail) =>
        new(true, FfmpegEncoderFailureReasonCode.None, 0, stderrTail, null);

    internal static FfmpegEncoderCompletionOutcome Failure(
        FfmpegEncoderFailureReasonCode reasonCode,
        int? exitCode,
        string stderrTail,
        Exception? exception = null
    ) => new(false, reasonCode, exitCode, stderrTail, exception);
}

internal enum ReplayVideoLogStage
{
    EncoderStarted,
    FrameConsumeCallback,
    StderrReader,
    CaptureStarted,
    CaptureFinalized,
    Readback,
    CaptureRequest,
    RenderTextureRelease,
    MuxCallback,
    MuxProbe,
    MuxDrain,
    DebugStem,
    TempDelete,
    WavDelete,
    UiSuppression,
    SessionStarted,
    SessionEnded,
}

internal enum ReplayVideoDiagnosticReasonCode
{
    None,
    CallbackException,
    ReaderException,
    ReadbackFailed,
    ReadbackOutOfOrder,
    CaptureException,
    CleanupException,
    ProbeFailed,
    DrainFailed,
    StemPreserveFailed,
}
