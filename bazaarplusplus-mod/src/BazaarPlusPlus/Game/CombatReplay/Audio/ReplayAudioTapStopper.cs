#nullable enable
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CombatReplay.Audio;

internal static class ReplayAudioTapStopper
{
    public static List<string> StopAndCollectUsableWavPaths(
        List<IReplayAudioCaptureTap> taps,
        string recordingId,
        out IReadOnlyList<ReplayAudioCaptureResult> captureResults
    )
    {
        var results = Stop(taps, recordingId);
        captureResults = results;
        var usableWavPaths = new List<string>(results.Count);
        foreach (var result in results)
        {
            if (result.Usable)
                usableWavPaths.Add(result.WavPath);
        }

        return usableWavPaths;
    }

    public static List<ReplayAudioCaptureResult> Stop(
        List<IReplayAudioCaptureTap> taps,
        string recordingId
    )
    {
        if (taps == null)
            throw new ArgumentNullException(nameof(taps));

        var snapshot = new List<IReplayAudioCaptureTap>(taps);
        taps.Clear();

        var results = new List<ReplayAudioCaptureResult>(snapshot.Count);
        foreach (var tap in snapshot)
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
            var capturePointLabel = tap.CapturePointLabel;
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
                CombatReplayVideoLogEvents.AudioCaptureCompleted,
                () =>
                    [
                        CombatReplayVideoLogEvents.AudioCompletedRecordingId.Bind(recordingId),
                        CombatReplayVideoLogEvents.AudioCompletedBackend.Bind(tap.Backend),
                        CombatReplayVideoLogEvents.AudioCompletedUsable.Bind(usable),
                        CombatReplayVideoLogEvents.AudioCompletedSampleFloatCount.Bind(
                            sampleFloats
                        ),
                        CombatReplayVideoLogEvents.AudioCompletedRmsDb.Bind(
                            FormatAmplitudeDb(rmsAmplitude)
                        ),
                        CombatReplayVideoLogEvents.AudioCompletedPeakDb.Bind(
                            FormatAmplitudeDb(peakAmplitude)
                        ),
                        CombatReplayVideoLogEvents.AudioCompletedSizeBytes.Bind(fileSize),
                        CombatReplayVideoLogEvents.AudioCompletedWavPath.Bind(wavPath),
                    ]
            );

            if (!usable)
                DeleteWavBestEffort(wavPath);

            results.Add(
                new ReplayAudioCaptureResult
                {
                    WavPath = wavPath,
                    CapturedAnySamples = capturedAnySamples,
                    CapturedSampleFloats = sampleFloats,
                    FileSizeBytes = fileSize,
                    CapturePointLabel = capturePointLabel,
                    RmsAmplitude = rmsAmplitude,
                    PeakAmplitude = peakAmplitude,
                    Usable = usable,
                    Backend = tap.Backend,
                    SampleRateHz = tap.SampleRateHz,
                    Channels = tap.Channels,
                    SampleFormat = tap.SampleFormat,
                    FailureReason = failureReason,
                    FailureException = failureException,
                }
            );
        }

        return results;
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
