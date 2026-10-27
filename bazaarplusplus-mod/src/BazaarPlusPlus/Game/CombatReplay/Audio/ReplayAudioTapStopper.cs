#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

/// <summary>What stopping a recording's taps left: each tap's result, and the WAVs worth muxing.</summary>
internal sealed class ReplayAudioStopOutcome
{
    internal static readonly ReplayAudioStopOutcome Empty = new(
        Array.Empty<ReplayAudioCaptureResult>()
    );

    internal ReplayAudioStopOutcome(IReadOnlyList<ReplayAudioCaptureResult> results)
    {
        Results = results;
        var usable = new List<string>(results.Count);
        foreach (var result in results)
        {
            if (result.Usable)
                usable.Add(result.WavPath);
        }
        UsableWavPaths = usable;
    }

    internal IReadOnlyList<ReplayAudioCaptureResult> Results { get; }

    /// <summary>Closed WAVs whose tap pushed PCM; header-only WAVs are already deleted.</summary>
    internal IReadOnlyList<string> UsableWavPaths { get; }
}

/// <summary>
/// Stops a recording's taps in two steps: <see cref="Detach"/> on the main thread hands the taps
/// over, and <see cref="Stop"/> runs off it, because a tap's stop joins its capture thread and
/// then releases the backend (WASAPI COM objects, or the CoreAudio IOProc, aggregate device and
/// process tap), closes the WAV and checks the file.
/// </summary>
internal static class ReplayAudioTapStopper
{
    public static IReadOnlyList<IReplayAudioCaptureTap> Detach(List<IReplayAudioCaptureTap> taps)
    {
        if (taps == null)
            throw new ArgumentNullException(nameof(taps));

        var snapshot = taps.ToArray();
        taps.Clear();
        return snapshot;
    }

    public static ReplayAudioStopOutcome Stop(
        IReadOnlyList<IReplayAudioCaptureTap> taps,
        string recordingId
    )
    {
        if (taps == null)
            throw new ArgumentNullException(nameof(taps));
        if (taps.Count == 0)
            return ReplayAudioStopOutcome.Empty;

        var results = new List<ReplayAudioCaptureResult>(taps.Count);
        foreach (var tap in taps)
        {
            Exception? stopException = null;
            try
            {
                tap.Stop();
            }
            catch (Exception ex)
            {
                stopException = ex;
            }

            var capturedAnySamples = tap.CapturedAnySamples;
            var sampleFloats = tap.CapturedSampleFloats;
            var wavPath = tap.WavFilePath;
            var rmsAmplitude = tap.RmsAmplitude;
            var peakAmplitude = tap.PeakAmplitude;
            var failureReason =
                stopException == null
                    ? tap.FailureReason
                    : ReplayAudioFailureReasonCode.BackendStopFailed;
            var failureException = stopException ?? tap.FailureException;

            var fileSize = TryGetFileSize(wavPath);
            var usable =
                failureReason == ReplayAudioFailureReasonCode.None
                && IsUsable(capturedAnySamples, wavPath);
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.audio_capture.completed"
                ),
                () =>
                    [
                        ("recording_id", recordingId, BppLogCorrelationPolicy.Short),
                        ("backend", tap.Backend),
                        ("usable", usable),
                        ("sample_float_count", sampleFloats),
                        ("rms_db", FormatAmplitudeDb(rmsAmplitude)),
                        ("peak_db", FormatAmplitudeDb(peakAmplitude)),
                        ("size_bytes", fileSize),
                        ("wav_path", wavPath),
                    ]
            );

            if (!usable)
                DeleteWavBestEffort(wavPath);

            results.Add(
                new ReplayAudioCaptureResult
                {
                    WavPath = wavPath,
                    Usable = usable,
                    FailureReason = failureReason,
                    FailureException = failureException,
                }
            );
        }

        return new ReplayAudioStopOutcome(results);
    }

    public static bool IsUsable(bool capturedAnySamples, string wavPath)
    {
        return capturedAnySamples && !string.IsNullOrWhiteSpace(wavPath) && File.Exists(wavPath);
    }

    private static long TryGetFileSize(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return new FileInfo(path).Length;
        }
        catch
        {
            // best-effort diagnostic only
        }

        return 0;
    }

    private static double FormatAmplitudeDb(double amplitude)
    {
        if (amplitude <= 0 || double.IsNaN(amplitude) || double.IsInfinity(amplitude))
            return -120.0;

        return Math.Round(20.0 * Math.Log10(amplitude), 1);
    }

    private static void DeleteWavBestEffort(string? wavPath)
    {
        if (string.IsNullOrEmpty(wavPath))
            return;

        try
        {
            if (File.Exists(wavPath))
                File.Delete(wavPath);
        }
        catch
        {
            // best-effort
        }
    }
}
