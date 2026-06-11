#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Audio;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class CombatReplayVideoRecorder : MonoBehaviour
{
    private IBppServices? _services;
    private CombatReplayVideoMetadataStore? _metadataStore;
    private IDisposable? _startingSubscription;
    private IDisposable? _endedSubscription;
    private ReplayVideoCaptureSession? _activeSession;
    private Coroutine? _captureCoroutine;
    private IDisposable? _uiSuppressionScope;
    private string? _activeRecordingTempPath;
    private string? _activeRecordingFinalPath;
    private readonly List<IReplayAudioCaptureTap> _audioTaps = new();
    private List<string>? _activeAudioWavPaths;
    private ReplayVideoAudioMuxer? _muxer;
    private readonly System.Collections.Generic.List<System.Threading.Tasks.Task> _muxTasks = new();
    private readonly object _muxTasksLock = new();

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        var runLogDatabasePath = services.Paths.RunLogDatabasePath;
        if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
        {
            try
            {
                _metadataStore = new CombatReplayVideoMetadataStore(runLogDatabasePath);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CombatReplayVideo",
                    $"Failed to open replay video metadata store: {ex.Message}. Metadata will not be persisted."
                );
                _metadataStore = null;
            }
        }

        // ComponentMount.Mount calls AddComponent (which fires OnEnable synchronously on an
        // active host) BEFORE this initializer runs, so the first OnEnable saw a null _services
        // and could not subscribe. Now that services are available, ensure we are subscribed.
        if (isActiveAndEnabled)
            EnsureEventSubscriptions();
    }

    private void OnEnable()
    {
        EnsureEventSubscriptions();
    }

    private void EnsureEventSubscriptions()
    {
        var services = _services;
        if (services == null || _startingSubscription != null)
            return;

        _startingSubscription = services.EventBus.Subscribe<CombatReplayPlaybackStarting>(
            OnPlaybackStarting
        );
        _endedSubscription = services.EventBus.Subscribe<CombatReplayPlaybackEnded>(
            OnPlaybackEnded
        );
        BppLog.Info("CombatReplayVideo", "Subscribed to combat replay playback events.");
    }

    private void OnDisable()
    {
        _startingSubscription?.Dispose();
        _startingSubscription = null;
        _endedSubscription?.Dispose();
        _endedSubscription = null;

        AbortActiveSession("recorder-disabled");
    }

    private void OnDestroy()
    {
        AbortActiveSession("recorder-destroyed");

        // Best-effort drain of any in-flight background mux tasks so a recording
        // that just ended gets a chance to produce its final file before the app
        // tears down. This is the only viable shutdown seam (no Application.quitting
        // hook); un-drained temps are acceptable and reclaimed on the next launch.
        System.Threading.Tasks.Task[] pending;
        lock (_muxTasksLock)
        {
            pending = _muxTasks.ToArray();
        }

        if (pending.Length > 0)
        {
            try
            {
                System.Threading.Tasks.Task.WaitAll(pending, 4000);
            }
            catch (Exception ex)
            {
                BppLog.Debug("CombatReplayVideo", $"Mux drain on destroy incomplete: {ex.Message}");
            }
        }
    }

    private void OnPlaybackStarting(CombatReplayPlaybackStarting evt)
    {
        if (evt == null || string.IsNullOrWhiteSpace(evt.BattleId))
            return;

        BppLog.Info(
            "CombatReplayVideo",
            $"OnPlaybackStarting battle={evt.BattleId} recordVideo={evt.RecordVideo} activeSession={_activeSession != null}"
        );

        if (_activeSession != null)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Replay playback started for {evt.BattleId} while a previous capture session was still active; aborting old session."
            );
            AbortActiveSession("superseded");
        }

        if (!evt.RecordVideo)
            return;

        var services = _services;
        if (services == null)
            return;

        var pluginsDirectoryPath = services.Paths.PluginsDirectoryPath;
        var gate = CombatReplayRecordingGate.Evaluate(
            pluginsDirectoryPath,
            services.Paths.CombatReplayVideoDirectoryPath
        );
        if (!gate.CanRecord)
        {
            switch (gate.Blocker)
            {
                case CombatReplayRecordingBlocker.NoAsyncGpuReadback:
                    BppLog.Info(
                        "CombatReplayVideo",
                        "SystemInfo.supportsAsyncGPUReadback is false on this device; video recording is disabled."
                    );
                    break;
                case CombatReplayRecordingBlocker.FfmpegUnavailable:
                    BppLog.Warn(
                        "CombatReplayVideo",
                        $"Recording requested for {evt.BattleId} but FFmpeg could not be resolved (plugins='{pluginsDirectoryPath}'); skipping capture."
                    );
                    break;
                default:
                    BppLog.Warn(
                        "CombatReplayVideo",
                        "CombatReplayVideoDirectoryPath is not configured; cannot record replay video."
                    );
                    break;
            }
            return;
        }

        var request = BuildCaptureRequest(evt, gate.FfmpegExecutable!, gate.VideoDirectoryPath!);
        if (request == null)
            return;

        try
        {
            BeginRecording(request, services);
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CombatReplayVideo",
                $"Failed to begin replay video recording for {evt.BattleId}.",
                ex
            );
            CleanupAfterAbort();
        }
    }

    private void OnPlaybackEnded(CombatReplayPlaybackEnded evt)
    {
        if (evt == null || _activeSession == null)
            return;

        var session = _activeSession;
        var reason = evt.Reason ?? (evt.Failed ? "playback-failed" : "playback-ended");

        try
        {
            StopCaptureCoroutine();
            var result = session.Finalize(reason);
            // Closes + unlocks the WAVs before the muxer reads them, returning
            // only paths whose tap actually pushed PCM. Header-only WAVs are
            // deleted inside StopAudioTaps.
            var wavPaths = StopAudioTaps();

            // Capture locals before nulling instance fields: the mux runs on a
            // background thread after this method returns, so it must not read
            // mutable recorder state.
            var tempVideoPath = _activeRecordingTempPath ?? result.OutputFilePath;
            var finalPath = _activeRecordingFinalPath ?? StripTempSuffix(result.OutputFilePath);
            var videoDir = _services?.Paths.CombatReplayVideoDirectoryPath;
            var store = _metadataStore;

            DisposeUiState();
            _activeSession = null;
            _activeRecordingTempPath = null;
            _activeRecordingFinalPath = null;
            _activeAudioWavPaths = null;

            DispatchMuxOrPromote(result, tempVideoPath, finalPath, videoDir, wavPaths, store);

            BppLog.Info(
                "CombatReplayVideo",
                $"Recording for {result.BattleId} stopped with status={result.Status} captured={result.CapturedFrames} dropped={result.DroppedFrames}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error(
                "CombatReplayVideo",
                $"Failed to finalize replay video recording for {evt.BattleId}.",
                ex
            );
            var wavPaths = _activeAudioWavPaths;
            StopAudioTaps();
            try
            {
                session.Dispose();
            }
            catch
            {
                // ignore
            }
            DisposeUiState();
            DeleteTempFile();
            DeleteWavBestEffort(wavPaths);
            _activeSession = null;
            _activeRecordingTempPath = null;
            _activeRecordingFinalPath = null;
            _activeAudioWavPaths = null;
        }
    }

    // Exception-safe, idempotent. Stops all audio taps (signals stop, joins the WAV
    // writer threads, closes the files) so the WAVs are complete and unlocked before
    // the muxer reads them. Returns only WAV paths whose tap captured at least
    // one PCM sample, so header-only WAVs cannot truncate the video during -shortest.
    private List<string> StopAudioTaps()
    {
        var taps = new List<IReplayAudioCaptureTap>(_audioTaps);
        _audioTaps.Clear();

        var capturedWavPaths = new List<string>(taps.Count);
        foreach (var tap in taps)
        {
            // Read before Stop(): CapturedAnySamples is updated by the capture thread and
            // remains available after teardown, but this keeps the decision explicit.
            var capturedAnySamples = tap.CapturedAnySamples;
            var sampleFloats = tap.CapturedSampleFloats;
            var wavPath = tap.WavFilePath;

            try
            {
                tap.Stop();
            }
            catch (Exception ex)
            {
                BppLog.Warn("CombatReplayAudio", $"Audio tap stop failed: {ex.Message}");
            }

            var fileSize = FfmpegRawVideoEncoder.TryGetFileSize(wavPath);
            var rmsDb = FormatAmplitudeDb(tap.RmsAmplitude);
            var peakDb = FormatAmplitudeDb(tap.PeakAmplitude);
            BppLog.Info(
                "CombatReplayAudio",
                $"Audio tap stopped source={tap.CapturePointLabel} captured={capturedAnySamples} sampleFloats={sampleFloats} rms_db={rmsDb} peak_db={peakDb} size_bytes={fileSize} file={wavPath}"
            );

            if (capturedAnySamples && File.Exists(wavPath))
                capturedWavPaths.Add(wavPath);
            else
                DeleteWavBestEffort(wavPath);
        }

        return capturedWavPaths;
    }

    private static string FormatAmplitudeDb(double amplitude)
    {
        if (amplitude <= 0 || double.IsNaN(amplitude) || double.IsInfinity(amplitude))
            return "-inf";

        return (20.0 * Math.Log10(amplitude)).ToString(
            "F1",
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    // Central post-finalize logic. Called from the OnPlaybackEnded success path with
    // LOCALS (never instance fields) so the async mux never races recorder state.
    //   - Not Completed: keep the current silent behavior synchronously.
    //   - Completed but no captured audio (no WAV, or a header-only WAV the tap
    //     opened but never fed): promote the silent video synchronously.
    //   - Completed + captured audio: dispatch the ffmpeg mux on a background thread;
    //     SaveFinish moves into the mux completion callback (always COMPLETED — audio
    //     degradation is never a video failure).
    private void DispatchMuxOrPromote(
        ReplayVideoCaptureResult result,
        string tempVideoPath,
        string finalPath,
        string? videoDir,
        IReadOnlyList<string>? wavPaths,
        CombatReplayVideoMetadataStore? store
    )
    {
        if (result.Status != ReplayVideoCaptureStatus.Completed)
        {
            FinalizeOutputFileFor(result, tempVideoPath, finalPath);
            TrySaveFinishMetadataFor(store, videoDir, finalPath, result, null);
            DeleteWavBestEffort(wavPaths);
            return;
        }

        // Gate the mux on captured-any-samples, not merely File.Exists(wavPath):
        // WavStreamWriter writes a 44-byte header on open, so a tap that captured
        // zero PCM still leaves a present-but-empty WAV. Muxing that with -shortest
        // yields a zero-duration output and would destroy the good silent video.
        var usableWavPaths = VideoProcessHelpers.GetExistingWavPaths(wavPaths);
        if (usableWavPaths.Count == 0)
        {
            // No usable audio track: promote the silent video to the final path now
            // and discard the empty WAV.
            DeleteWavBestEffort(wavPaths);
            try
            {
                ReplayVideoAudioMuxer.PromoteSilentToFinal(tempVideoPath, finalPath);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CombatReplayVideo",
                    $"Failed to promote silent video '{tempVideoPath}' to '{finalPath}': {ex.Message}"
                );
            }

            TrySaveFinishMetadataFor(
                store,
                videoDir,
                finalPath,
                result,
                FfmpegRawVideoEncoder.TryGetFileSize(finalPath)
            );
            return;
        }

        var ffmpegExecutable = FfmpegLocator.Resolve(_services?.Paths.PluginsDirectoryPath);
        if (string.IsNullOrEmpty(ffmpegExecutable))
        {
            // Cannot mux without ffmpeg: keep the silent video as the final product.
            try
            {
                ReplayVideoAudioMuxer.PromoteSilentToFinal(tempVideoPath, finalPath);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CombatReplayVideo",
                    $"Failed to promote silent video '{tempVideoPath}' to '{finalPath}': {ex.Message}"
                );
            }

            DeleteWavBestEffort(usableWavPaths);
            TrySaveFinishMetadataFor(
                store,
                videoDir,
                finalPath,
                result,
                FfmpegRawVideoEncoder.TryGetFileSize(finalPath)
            );
            return;
        }

        _muxer ??= new ReplayVideoAudioMuxer(ffmpegExecutable!);

        // Background mux: the menu transition must not block on remux. The muxer
        // gracefully promotes the silent video on any failure, so the completion
        // callback always sees a usable final file. SaveFinish reports COMPLETED
        // with the recomputed size of whatever landed (muxed or promoted).
        var task = _muxer.DispatchAsync(
            tempVideoPath,
            usableWavPaths,
            finalPath,
            onCompleted: mux =>
                TrySaveFinishMetadataFor(
                    store,
                    videoDir,
                    mux.FinalFilePath,
                    result,
                    mux.FileSizeBytes
                )
        );

        lock (_muxTasksLock)
        {
            _muxTasks.RemoveAll(t => t.IsCompleted);
            _muxTasks.Add(task);
        }
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

    private static void DeleteWavBestEffort(IReadOnlyList<string>? wavPaths)
    {
        if (wavPaths == null)
            return;

        foreach (var wavPath in wavPaths)
            DeleteWavBestEffort(wavPath);
    }

    private void BeginRecording(ReplayVideoCaptureRequest request, IBppServices services)
    {
        var session = new ReplayVideoCaptureSession(request);
        try
        {
            session.Start();
        }
        catch
        {
            session.Dispose();
            throw;
        }

        _activeSession = session;
        _activeRecordingTempPath = request.OutputFilePath;
        _activeRecordingFinalPath = StripTempSuffix(request.OutputFilePath);

        StartAudioTaps(request.OutputFilePath);

        _uiSuppressionScope = BeginUiSuppression();

        TrySaveStartMetadata(request, services);

        _captureCoroutine = StartCoroutine(CaptureLoop(session));

        BppLog.Info(
            "CombatReplayVideo",
            $"Recording started battle={request.BattleId} source={request.Source} -> {request.OutputFilePath}"
        );
    }

    private void StartAudioTaps(string tempVideoPath)
    {
        // Audio is additive: capture failure must never abort the video recording. Capture the device
        // output (loopback) so we record exactly what the player hears — music, settlement, and the
        // spatialised combat/board SFX that no FMOD channel group exposes.
        var wavPath = ReplayVideoAudioTapPlan.DeriveAudioWavPath(tempVideoPath);
        _activeAudioWavPaths = new List<string> { wavPath };
        TryStartAudioTap(wavPath);

        if (_audioTaps.Count == 0)
        {
            DeleteWavBestEffort(_activeAudioWavPaths);
            _activeAudioWavPaths = null;
        }
    }

    private void TryStartAudioTap(string wavPath)
    {
        try
        {
            IReplayAudioCaptureTap tap = ReplayAudioCaptureFactory.Create(wavPath);
            if (tap.TryStart())
            {
                _audioTaps.Add(tap);
                return;
            }

            tap.Dispose();
            DeleteWavBestEffort(wavPath);
        }
        catch (Exception ex)
        {
            BppLog.Warn("CombatReplayAudio", $"Audio capture unavailable: {ex.Message}");
            DeleteWavBestEffort(wavPath);
        }
    }

    private void TrySaveStartMetadata(ReplayVideoCaptureRequest request, IBppServices services)
    {
        var store = _metadataStore;
        if (store == null)
            return;

        try
        {
            var videoDirectoryPath = services.Paths.CombatReplayVideoDirectoryPath;
            var relativePath = ComputeRelativePath(
                videoDirectoryPath,
                _activeRecordingFinalPath ?? request.OutputFilePath
            );

            store.SaveStart(
                new VideoRecordingStarted
                {
                    VideoId = request.VideoId,
                    BattleId = request.BattleId,
                    Source = request.Source.ToString(),
                    VideoRelativePath = relativePath,
                    Width = request.Width,
                    Height = request.Height,
                    Fps = request.Fps,
                    Codec = "libx264",
                    Crf = request.Crf,
                    Preset = request.Preset,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Failed to save start metadata for video {request.VideoId}: {ex.Message}"
            );
        }
    }

    // Parameterized so it is race-free under the async mux: every input is a value
    // captured on the main thread before instance fields are nulled. Safe to call on
    // a background thread because the store opens a fresh SQLite connection per call.
    // FileSizeBytes uses overrideFileSize when provided (the recomputed size of the
    // final/muxed file), otherwise the result's own size.
    private void TrySaveFinishMetadataFor(
        CombatReplayVideoMetadataStore? store,
        string? videoDir,
        string finalPath,
        ReplayVideoCaptureResult result,
        long? overrideFileSize
    )
    {
        if (store == null)
            return;

        try
        {
            var relativePath = ComputeRelativePath(videoDir, finalPath);
            var endedAt = result.EndedAtUtc ?? DateTimeOffset.UtcNow;
            var status = result.Status switch
            {
                ReplayVideoCaptureStatus.Completed => "COMPLETED",
                ReplayVideoCaptureStatus.Failed => "FAILED",
                _ => "FAILED",
            };

            store.SaveFinish(
                new VideoRecordingFinished
                {
                    VideoId = result.VideoId,
                    VideoRelativePath = relativePath,
                    EndedAtUtc = endedAt,
                    DurationMs = result.DurationMs,
                    CapturedFrames = result.CapturedFrames,
                    DroppedFrames = result.DroppedFrames,
                    FileSizeBytes = overrideFileSize ?? result.FileSizeBytes,
                    Status = status,
                    Error = result.Error,
                }
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Failed to save finish metadata for video {result.VideoId}: {ex.Message}"
            );
        }
    }

    private static string ComputeRelativePath(string? rootDirectory, string filePath)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(filePath))
            return filePath ?? string.Empty;

        try
        {
            var rootFull = Path.GetFullPath(rootDirectory);
            var fileFull = Path.GetFullPath(filePath);
            if (
                fileFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)
                && fileFull.Length > rootFull.Length
            )
            {
                var trimmed = fileFull.Substring(rootFull.Length);
                return trimmed.TrimStart(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                );
            }
        }
        catch
        {
            // fall through
        }

        return filePath;
    }

    private IEnumerator CaptureLoop(ReplayVideoCaptureSession session)
    {
        var waitForEndOfFrame = new WaitForEndOfFrame();
        while (session.IsActive && _activeSession == session)
        {
            yield return waitForEndOfFrame;
            if (!session.IsActive || _activeSession != session)
                break;
            session.CaptureFrameIfDue();
        }
    }

    private void StopCaptureCoroutine()
    {
        if (_captureCoroutine != null)
        {
            try
            {
                StopCoroutine(_captureCoroutine);
            }
            catch
            {
                // ignore
            }
            _captureCoroutine = null;
        }
    }

    private void AbortActiveSession(string reason)
    {
        var session = _activeSession;
        _activeSession = null;

        StopCaptureCoroutine();
        // Tear the tap down synchronously: stop + join before any new recording's
        // capture thread starts (covers superseded, OnDisable, OnDestroy, and the
        // scene-change-driven OnDisable). Abort never muxes — it deletes temps.
        var wavPaths = _activeAudioWavPaths;
        StopAudioTaps();

        if (session != null)
        {
            try
            {
                var result = session.Finalize(reason);
                var tempPath = _activeRecordingTempPath ?? result.OutputFilePath;
                var finalPath = _activeRecordingFinalPath ?? StripTempSuffix(result.OutputFilePath);
                FinalizeOutputFileFor(result, tempPath, finalPath);
                TrySaveFinishMetadataFor(
                    _metadataStore,
                    _services?.Paths.CombatReplayVideoDirectoryPath,
                    finalPath,
                    result,
                    null
                );
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    "CombatReplayVideo",
                    $"Abort finalize failed reason={reason}: {ex.Message}"
                );
            }
            finally
            {
                try
                {
                    session.Dispose();
                }
                catch
                {
                    // ignore
                }
            }
        }

        DisposeUiState();
        DeleteTempFile();
        DeleteWavBestEffort(wavPaths);
        _activeRecordingTempPath = null;
        _activeRecordingFinalPath = null;
        _activeAudioWavPaths = null;
    }

    private void CleanupAfterAbort()
    {
        var wavPaths = _activeAudioWavPaths;
        StopAudioTaps();
        DisposeUiState();
        DeleteTempFile();
        DeleteWavBestEffort(wavPaths);
        _activeRecordingTempPath = null;
        _activeRecordingFinalPath = null;
        _activeAudioWavPaths = null;
    }

    private static IDisposable? BeginUiSuppression()
    {
        try
        {
            // The combat status bar is intentionally NOT suppressed here: during a recorded replay
            // it stays visible just like in a normal replay, so it is captured into the MP4 (the
            // recorder uses full-screen ScreenCapture). The remaining BPP overlays stay suppressed
            // to keep them out of the recording.
            return BppUiChromeSuppression.Begin(BppUiChromeSuppressionMode.ReplayRecording);
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to begin BPP overlay suppression scope: {ex.Message}"
            );
            return null;
        }
    }

    private void DisposeUiState()
    {
        if (_uiSuppressionScope != null)
        {
            try
            {
                _uiSuppressionScope.Dispose();
            }
            catch
            {
                // ignore
            }
            _uiSuppressionScope = null;
        }
    }

    private ReplayVideoCaptureRequest? BuildCaptureRequest(
        CombatReplayPlaybackStarting evt,
        string ffmpegExecutable,
        string videoDirectoryPath
    )
    {
        // Fixed encoder defaults (formerly the [CombatReplayVideo] cfg knobs).
        const int fps = 30;
        const int crf = 23;
        const string preset = "veryfast";

        var width = Screen.width;
        var height = Screen.height;
        if (width <= 0 || height <= 0)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Cannot record video with invalid size {width}x{height}; skipping."
            );
            return null;
        }

        // Round to even dimensions for yuv420p compatibility.
        if ((width & 1) != 0)
            width--;
        if ((height & 1) != 0)
            height--;

        const int maxQueued = 90;

        var nowLocal = DateTimeOffset.Now;
        var datePart = nowLocal.ToString("yyyy-MM-dd");
        var stampPart = nowLocal.ToString("yyyyMMdd-HHmmss");
        var sanitizedBattleId = SanitizeForPath(evt.BattleId);
        var outputDirectory = Path.Combine(videoDirectoryPath, datePart);
        var finalFileName = $"{sanitizedBattleId}.{stampPart}.mp4";
        var tempFileName = $"{sanitizedBattleId}.{stampPart}.recording.mp4";
        var outputFilePath = Path.Combine(outputDirectory, tempFileName);

        return new ReplayVideoCaptureRequest
        {
            BattleId = evt.BattleId,
            Source = evt.Source,
            FfmpegExecutable = ffmpegExecutable,
            OutputFilePath = outputFilePath,
            OutputDirectoryPath = outputDirectory,
            Width = width,
            Height = height,
            Fps = fps,
            Crf = crf,
            Preset = preset,
            MaxQueuedFrames = maxQueued,
        };
    }

    // Parameterized version of the old FinalizeOutputFile. Takes explicit temp/final
    // paths so it no longer depends on _activeRecordingTempPath/_activeRecordingFinalPath
    // (which are nulled before the async mux runs). Not Completed -> delete the temp;
    // Completed -> promote the temp to the final path (the lifted File.Move sequence
    // lives once in ReplayVideoAudioMuxer.PromoteSilentToFinal).
    private void FinalizeOutputFileFor(
        ReplayVideoCaptureResult result,
        string tempPath,
        string finalPath
    )
    {
        if (result.Status != ReplayVideoCaptureStatus.Completed)
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                BppLog.Debug(
                    "CombatReplayVideo",
                    $"Failed to delete temp recording '{tempPath}': {ex.Message}"
                );
            }
            return;
        }

        try
        {
            ReplayVideoAudioMuxer.PromoteSilentToFinal(tempPath, finalPath);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CombatReplayVideo",
                $"Failed to rename recording '{tempPath}' to '{finalPath}': {ex.Message}"
            );
        }
    }

    private void DeleteTempFile()
    {
        var tempPath = _activeRecordingTempPath;
        if (string.IsNullOrWhiteSpace(tempPath))
            return;

        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            BppLog.Debug(
                "CombatReplayVideo",
                $"Failed to delete temp recording '{tempPath}': {ex.Message}"
            );
        }
    }

    private static string StripTempSuffix(string tempPath)
    {
        const string suffix = ".recording.mp4";
        if (string.IsNullOrEmpty(tempPath) || !tempPath.EndsWith(suffix, StringComparison.Ordinal))
            return tempPath;

        return tempPath.Substring(0, tempPath.Length - suffix.Length) + ".mp4";
    }

    private static string SanitizeForPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }
}
