#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Audio;

internal enum ReplayAudioBackend
{
    WasapiLoopback,
    CoreAudioProcessTap,
    Unsupported,
}

internal enum ReplayAudioFailureReasonCode
{
    None,
    UnsupportedPlatform,
    BackendStartFailed,
    CaptureLoopFailed,
    BackendStopFailed,
    WavCloseFailed,
}

internal readonly record struct ReplayAudioCaptureStartOutcome(
    bool Started,
    ReplayAudioFailureReasonCode ReasonCode,
    Exception? Exception
)
{
    internal static ReplayAudioCaptureStartOutcome Success() =>
        new(true, ReplayAudioFailureReasonCode.None, null);

    internal static ReplayAudioCaptureStartOutcome Failure(
        ReplayAudioFailureReasonCode reasonCode,
        Exception? exception = null
    ) => new(false, reasonCode, exception);
}

internal interface IReplayAudioCaptureTap : IDisposable
{
    // Call on the Unity main thread.
    ReplayAudioCaptureStartOutcome TryStart();

    // Idempotent. Closes the WAV before the muxer reads it.
    void Stop();

    bool IsCapturing { get; }

    bool CapturedAnySamples { get; }

    long CapturedSampleFloats { get; }

    string WavFilePath { get; }

    ReplayAudioBackend Backend { get; }

    int SampleRateHz { get; }

    int Channels { get; }

    string SampleFormat { get; }

    ReplayAudioFailureReasonCode FailureReason { get; }

    Exception? FailureException { get; }

    /// <summary>RMS amplitude of the captured signal (0..1), for diagnostics.</summary>
    double RmsAmplitude { get; }

    /// <summary>Peak absolute amplitude of the captured signal (0..1), for diagnostics.</summary>
    float PeakAmplitude { get; }
}
