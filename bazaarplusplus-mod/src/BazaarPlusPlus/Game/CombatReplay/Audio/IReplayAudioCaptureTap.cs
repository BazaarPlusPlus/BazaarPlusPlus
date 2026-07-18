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

/// <summary>
/// A replay-audio capture source that writes a streaming WAV the recorder later muxes into the MP4.
/// Implemented by WasapiLoopbackCaptureTap (Windows), CoreAudioProcessTapCaptureTap (macOS ≥ 15), and
/// UnsupportedPlatformAudioCapture (no-op fallback) so the recorder can swap capture strategies without
/// changing its lifecycle/teardown code.
/// </summary>
internal interface IReplayAudioCaptureTap : IDisposable
{
    /// <summary>MAIN thread. Starts capture and returns a typed success/failure outcome.</summary>
    ReplayAudioCaptureStartOutcome TryStart();

    /// <summary>Idempotent teardown. Flushes and closes the WAV so the muxer can read it.</summary>
    void Stop();

    /// <summary>True once capture is active.</summary>
    bool IsCapturing { get; }

    /// <summary>True once at least one PCM sample has been written.</summary>
    bool CapturedAnySamples { get; }

    /// <summary>Total interleaved float samples captured.</summary>
    long CapturedSampleFloats { get; }

    /// <summary>The WAV path this tap writes to.</summary>
    string WavFilePath { get; }

    /// <summary>Human-readable capture-source label for diagnostics.</summary>
    string CapturePointLabel { get; }

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
