#nullable enable
using System.Collections;
using System.Collections.Concurrent;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Audio;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

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
    private string? _activeFfmpegExecutable;
    private ReplayVideoAudioMuxer? _muxer;
    private ReplayVideoRecordingLifecycle _operations = null!;
    private ReplayVideoRecordingOperation? _activeOperation;
    private PreparedCurrentReplayRecording? _preparedCurrentReplay;
    private readonly ConcurrentQueue<CombatReplayVideoRecordingCompleted> _completionEvents = new();
    private readonly object _availabilitySync = new();
    private CurrentReplayRecorderAvailability _currentReplayAvailability = new(
        CurrentReplayRecorderAvailabilityPhase.Unavailable,
        "Video recorder is unavailable."
    );
    private Task? _availabilityTask;
    private int _availabilityGeneration;
    private ReplayVideoCaptureSettings _availabilitySettings;
    private string? _availabilityFfmpegExecutable;
    private string? _availabilityVideoDirectory;
    private ReplayVideoAudioStatus _activeAudioStatus = ReplayVideoAudioStatus.Silent;
    private ReplayVideoMetadataStatus _activeMetadataStatus = ReplayVideoMetadataStatus.Unavailable;
    private ReplayVideoRecordingReasonCode? _activeDegradationReason;
    private Exception? _activeDegradationException;
    private Exception? _metadataInitializationException;

    private void Awake()
    {
        _operations = new ReplayVideoRecordingLifecycle(null, OnOperationCompleted);
    }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out _);

        var runLogDatabasePath = services.Paths.RunLogDatabasePath;
        if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
        {
            try
            {
                _metadataStore = new CombatReplayVideoMetadataStore(runLogDatabasePath);
            }
            catch (Exception ex)
            {
                _metadataInitializationException = ex;
                _metadataStore = null;
            }
        }

        // ComponentMount.Mount calls AddComponent (which fires OnEnable synchronously on an
        // active host) BEFORE this initializer runs, so the first OnEnable saw a null _services
        // and could not subscribe. Now that services are available, ensure we are subscribed.
        if (isActiveAndEnabled)
            EnsureEventSubscriptions();
    }

    private void Update()
    {
        var eventBus = _services?.EventBus;
        if (eventBus == null)
            return;

        while (_completionEvents.TryDequeue(out var completed))
            eventBus.Publish(completed);
    }

    internal CurrentReplayRecorderAvailability PrepareCurrentReplayRecordingAvailability()
    {
        var services = _services;
        if (services == null || _metadataStore == null)
        {
            return SetAvailability(
                CurrentReplayRecorderAvailabilityPhase.Unavailable,
                "Video database is unavailable."
            );
        }

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            return SetAvailability(
                CurrentReplayRecorderAvailabilityPhase.Unavailable,
                "This device does not support asynchronous video capture."
            );
        }

        var videoDirectory = services.Paths.CombatReplayVideoDirectoryPath;
        if (string.IsNullOrWhiteSpace(videoDirectory))
        {
            return SetAvailability(
                CurrentReplayRecorderAvailabilityPhase.Unavailable,
                "Video output directory is unavailable."
            );
        }

        if (!ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out var settings))
        {
            return SetAvailability(
                CurrentReplayRecorderAvailabilityPhase.Unavailable,
                "The current game resolution cannot be recorded."
            );
        }

        lock (_availabilitySync)
        {
            if (
                _currentReplayAvailability.IsReady
                && _availabilitySettings == settings
                && string.Equals(
                    _availabilityVideoDirectory,
                    videoDirectory,
                    StringComparison.Ordinal
                )
            )
            {
                return _currentReplayAvailability;
            }

            if (_availabilityTask is { IsCompleted: false } && _availabilitySettings == settings)
                return _currentReplayAvailability;

            _availabilitySettings = settings;
            _availabilityVideoDirectory = videoDirectory;
            _availabilityFfmpegExecutable = null;
            _currentReplayAvailability = new CurrentReplayRecorderAvailability(
                CurrentReplayRecorderAvailabilityPhase.Preparing,
                "Preparing the video recorder."
            );
            var generation = ++_availabilityGeneration;
            var pluginsDirectory = services.Paths.PluginsDirectoryPath;
            _availabilityTask = Task.Run(() =>
            {
                try
                {
                    var ffmpeg = FfmpegLocator.Resolve(pluginsDirectory);
                    if (string.IsNullOrWhiteSpace(ffmpeg))
                    {
                        SetAvailabilityIfCurrent(
                            generation,
                            CurrentReplayRecorderAvailabilityPhase.Unavailable,
                            "FFmpeg is unavailable."
                        );
                        return;
                    }

                    FfmpegVideoEncoderSelector.Prewarm(
                        ffmpeg,
                        videoDirectory,
                        settings.Width,
                        settings.Height,
                        settings.Fps
                    );
                    lock (_availabilitySync)
                    {
                        if (
                            generation != _availabilityGeneration
                            || _availabilitySettings != settings
                            || !string.Equals(
                                _availabilityVideoDirectory,
                                videoDirectory,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            return;
                        }

                        _availabilityFfmpegExecutable = ffmpeg;
                        _currentReplayAvailability = new CurrentReplayRecorderAvailability(
                            CurrentReplayRecorderAvailabilityPhase.Ready,
                            null
                        );
                    }
                }
                catch (Exception ex)
                {
                    SetAvailabilityIfCurrent(
                        generation,
                        CurrentReplayRecorderAvailabilityPhase.Unavailable,
                        ex.Message
                    );
                }
            });
            return _currentReplayAvailability;
        }
    }

    internal CurrentReplayRecorderAvailability GetCurrentReplayRecordingAvailability()
    {
        lock (_availabilitySync)
            return _currentReplayAvailability;
    }

    internal CurrentReplayRecordingArmResult TryArmCurrentReplay(string battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return CurrentReplayRecordingArmResult.Failure("Battle id is unavailable.");
        if (_activeOperation != null || _preparedCurrentReplay != null)
            return CurrentReplayRecordingArmResult.Failure("A video recording is already active.");

        var services = _services;
        if (services == null || _metadataStore == null)
            return CurrentReplayRecordingArmResult.Failure("Video recorder is unavailable.");

        ReplayVideoCaptureSettings settings;
        string? ffmpeg;
        string? videoDirectory;
        CurrentReplayRecorderAvailability availability;
        lock (_availabilitySync)
        {
            availability = _currentReplayAvailability;
            settings = _availabilitySettings;
            ffmpeg = _availabilityFfmpegExecutable;
            videoDirectory = _availabilityVideoDirectory;
        }

        if (
            !availability.IsReady
            || string.IsNullOrWhiteSpace(ffmpeg)
            || string.IsNullOrWhiteSpace(videoDirectory)
        )
        {
            return CurrentReplayRecordingArmResult.Failure(
                availability.Reason ?? "Video recorder is still preparing."
            );
        }

        if (!ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out var currentSettings))
            return CurrentReplayRecordingArmResult.Failure(
                "The current game resolution cannot be recorded."
            );
        if (currentSettings != settings)
        {
            PrepareCurrentReplayRecordingAvailability();
            return CurrentReplayRecordingArmResult.Failure(
                "Game video settings changed; preparing the recorder again."
            );
        }

        var operation = _operations.Start(
            battleId,
            CombatReplayPlaybackSource.CurrentNative,
            DateTimeOffset.UtcNow
        );
        try
        {
            var evt = new CombatReplayPlaybackStarting
            {
                BattleId = battleId,
                Source = CombatReplayPlaybackSource.CurrentNative,
                RecordVideo = true,
            };
            var request = BuildCaptureRequest(
                operation.RecordingId,
                evt,
                ffmpeg,
                videoDirectory,
                settings
            );
            if (request == null)
            {
                _operations.CompletePreflight(
                    operation,
                    ReplayVideoRecordingReasonCode.InvalidDimensions
                );
                return CurrentReplayRecordingArmResult.Failure(
                    "The current game resolution cannot be recorded."
                );
            }

            try
            {
                _metadataStore.SaveStart(CreateStartedMetadata(request, services));
            }
            catch (Exception ex)
            {
                _operations.CompletePreflight(
                    operation,
                    ReplayVideoRecordingReasonCode.MetadataFailed,
                    ex
                );
                return CurrentReplayRecordingArmResult.Failure(
                    "The video database could not reserve this recording."
                );
            }

            _preparedCurrentReplay = new PreparedCurrentReplayRecording(operation, request);
            return CurrentReplayRecordingArmResult.Success(operation.RecordingId);
        }
        catch (Exception ex)
        {
            _operations.CompletePreflight(
                operation,
                ReplayVideoRecordingReasonCode.BeginException,
                ex
            );
            return CurrentReplayRecordingArmResult.Failure(ex.Message);
        }
    }

    internal void CancelArmedCurrentReplay(string recordingId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));

        var prepared = _preparedCurrentReplay;
        if (
            prepared == null
            || !string.Equals(prepared.Operation.RecordingId, recordingId, StringComparison.Ordinal)
        )
        {
            return;
        }

        _preparedCurrentReplay = null;
        TryMarkPreparedMetadataFailed(prepared, reason);
        _operations.CompletePreflight(
            prepared.Operation,
            ReplayVideoRecordingReasonCode.Aborted,
            reason: reason
        );
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
    }

    private void OnDisable()
    {
        _startingSubscription?.Dispose();
        _startingSubscription = null;
        _endedSubscription?.Dispose();
        _endedSubscription = null;

        AbortActiveSession("recorder-disabled");
        CancelPreparedCurrentReplay(ReplayVideoRecordingReasonCode.Aborted);
    }

    private void OnDestroy()
    {
        AbortActiveSession("recorder-destroyed");

        // Best-effort drain of any in-flight background finalize/mux tasks so a recording
        // that just ended gets a chance to produce its final file before the app
        // tears down. This is the only viable shutdown seam (no Application.quitting
        // hook); un-drained temps are acceptable and reclaimed on the next launch.
        try
        {
            if (!ReplayVideoAudioMuxer.TryDrainPendingForShutdown(TimeSpan.FromMilliseconds(4000)))
            {
                BppLog.DebugEvent(
                    CombatReplayVideoLogEvents.RecordingLifecycleObserved,
                    () =>
                        [
                            CombatReplayVideoLogEvents.LifecycleStage.Bind(
                                ReplayVideoLogStage.MuxDrain
                            ),
                            CombatReplayVideoLogEvents.LifecycleRecordingId.Bind(null),
                            CombatReplayVideoLogEvents.LifecycleBattleId.Bind(null),
                            CombatReplayVideoLogEvents.LifecyclePendingCount.Bind(
                                _operations.Count
                            ),
                        ]
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.VideoMuxDiagnosticObserved,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.MuxRecordingId.Bind(null),
                        CombatReplayVideoLogEvents.MuxStage.Bind(ReplayVideoLogStage.MuxDrain),
                        CombatReplayVideoLogEvents.MuxReasonCode.Bind(
                            ReplayVideoDiagnosticReasonCode.DrainFailed
                        ),
                        CombatReplayVideoLogEvents.MuxPath.Bind(null),
                        CombatReplayVideoLogEvents.MuxPendingCount.Bind(_operations.Count),
                    ]
            );
        }
        finally
        {
            // Sweep even after a successful drain so a completion exception can never strand a
            // registered operation.
            _operations.CompletePending(ReplayVideoRecordingReasonCode.ShutdownTimeout);
        }
    }

    private void OnPlaybackStarting(CombatReplayPlaybackStarting evt)
    {
        if (evt == null || string.IsNullOrWhiteSpace(evt.BattleId))
            return;

        if (evt.Source == CombatReplayPlaybackSource.CurrentNative)
        {
            BeginPreparedCurrentReplay(evt);
            return;
        }

        if (_activeOperation != null)
            AbortActiveSession("superseded");

        if (!evt.RecordVideo)
            return;

        var operation = _operations.Start(evt.BattleId, evt.Source, DateTimeOffset.UtcNow);

        try
        {
            var services = _services;
            if (services == null)
            {
                CompletePreflightFailure(
                    operation,
                    ReplayVideoRecordingReasonCode.OutputPathUnavailable
                );
                return;
            }

            var pluginsDirectoryPath = services.Paths.PluginsDirectoryPath;
            var gate = CombatReplayRecordingGate.Evaluate(
                pluginsDirectoryPath,
                services.Paths.CombatReplayVideoDirectoryPath
            );
            if (!gate.CanRecord)
            {
                CompletePreflightFailure(operation, MapGateBlocker(gate.Blocker));
                return;
            }

            var request = BuildCaptureRequest(
                operation.RecordingId,
                evt,
                gate.FfmpegExecutable!,
                gate.VideoDirectoryPath!
            );
            if (request == null)
            {
                CompletePreflightFailure(
                    operation,
                    ReplayVideoRecordingReasonCode.InvalidDimensions
                );
                return;
            }

            BeginRecording(operation, request, services);
        }
        catch (Exception ex)
        {
            _activeDegradationException = ex;
            AbortActiveSession("begin-exception");
            if (!operation.IsCompleted)
            {
                _operations.TryComplete(
                    operation,
                    new ReplayVideoRecordingCompletion
                    {
                        ReasonCode = ReplayVideoRecordingReasonCode.BeginException,
                        AudioStatus = ReplayVideoAudioStatus.Failed,
                        MetadataStatus = ReplayVideoMetadataStatus.Unavailable,
                        Exception = ex,
                    }
                );
            }
        }
    }

    private void BeginPreparedCurrentReplay(CombatReplayPlaybackStarting evt)
    {
        var prepared = _preparedCurrentReplay;
        _preparedCurrentReplay = null;
        if (
            prepared == null
            || !string.Equals(prepared.Operation.BattleId, evt.BattleId, StringComparison.Ordinal)
        )
        {
            if (prepared != null)
            {
                TryMarkPreparedMetadataFailed(prepared, "battle-id-mismatch");
                _operations.CompletePreflight(
                    prepared.Operation,
                    ReplayVideoRecordingReasonCode.Aborted
                );
            }
            return;
        }

        try
        {
            var services = _services;
            if (services == null)
            {
                TryMarkPreparedMetadataFailed(prepared, "recorder-services-unavailable");
                _operations.CompletePreflight(
                    prepared.Operation,
                    ReplayVideoRecordingReasonCode.OutputPathUnavailable
                );
                return;
            }

            BeginRecording(prepared.Operation, prepared.Request, services);
            services.EventBus.Publish(
                new CombatReplayVideoRecordingStarted
                {
                    RecordingId = prepared.Operation.RecordingId,
                    BattleId = prepared.Operation.BattleId,
                    Source = prepared.Operation.Source,
                }
            );
        }
        catch (Exception ex)
        {
            _activeDegradationException = ex;
            AbortActiveSession("begin-exception");
            TryMarkPreparedMetadataFailed(prepared, ex.Message);
            if (!prepared.Operation.IsCompleted)
            {
                _operations.TryComplete(
                    prepared.Operation,
                    new ReplayVideoRecordingCompletion
                    {
                        ReasonCode = ReplayVideoRecordingReasonCode.BeginException,
                        AudioStatus = ReplayVideoAudioStatus.Failed,
                        MetadataStatus = ReplayVideoMetadataStatus.Unavailable,
                        Exception = ex,
                    }
                );
            }
        }
    }

    private void OnPlaybackEnded(CombatReplayPlaybackEnded evt)
    {
        if (evt == null || _activeSession == null || _activeOperation == null)
            return;

        var session = _activeSession;
        var operation = _activeOperation;
        var reason = evt.Reason ?? (evt.Failed ? "playback-failed" : "playback-ended");
        ReplayVideoEncoderDrain? drain = null;

        try
        {
            StopCaptureCoroutine();
            drain = session.Finalize(reason);
            // Closes + unlocks the WAVs before the muxer reads them, returning
            // only paths whose tap actually pushed PCM. Header-only WAVs are
            // deleted inside ReplayAudioTapStopper.
            var wavPaths = ReplayAudioTapStopper.StopAndCollectUsableWavPaths(
                _audioTaps,
                operation.RecordingId,
                out var audioResults
            );
            ApplyAudioStopOutcomes(audioResults, wavPaths.Count > 0);

            // Freeze every continuation input before clearing the active recording. A superseding
            // replay may replace all _active* fields as soon as this method returns.
            var tempVideoPath = _activeRecordingTempPath ?? session.Request.OutputFilePath;
            var finalPath = _activeRecordingFinalPath ?? session.Request.FinalOutputFilePath;
            var ffmpegExecutable = _activeFfmpegExecutable;
            var videoDir = _services?.Paths.CombatReplayVideoDirectoryPath;
            var store = _metadataStore;
            var audioStatus = _activeAudioStatus;
            var metadataStatus = _activeMetadataStatus;
            var degradationReason = _activeDegradationReason;
            var degradationException = _activeDegradationException;
            var operations = _operations;
            _muxer ??= new ReplayVideoAudioMuxer();
            var muxer = _muxer;

            DisposeUiState();
            session.Dispose();

            var recordingContext = new RecordingFinalizeContext(
                operations,
                operation,
                drain,
                tempVideoPath,
                finalPath,
                wavPaths
            );
            var endedContext = new EndedRecordingFinalizeContext(
                recordingContext,
                muxer,
                ffmpegExecutable,
                store,
                videoDir,
                audioStatus,
                metadataStatus,
                degradationReason,
                degradationException
            );
            ReplayVideoAudioMuxer.DispatchTracked(() => CompleteEndedRecording(endedContext));
            ClearActiveState();
        }
        catch (Exception ex)
        {
            var wavPaths = _activeAudioWavPaths;
            ReplayAudioTapStopper.StopAndCollectUsableWavPaths(
                _audioTaps,
                operation.RecordingId,
                out _
            );
            var tempPath = _activeRecordingTempPath ?? session.Request.OutputFilePath;
            var finalPath = _activeRecordingFinalPath ?? session.Request.FinalOutputFilePath;
            DisposeUiState();
            session.Dispose();
            var operations = _operations;
            if (drain != null)
            {
                var recordingContext = new RecordingFinalizeContext(
                    operations,
                    operation,
                    drain,
                    tempPath,
                    finalPath,
                    wavPaths
                );
                ReplayVideoAudioMuxer.DispatchTracked(() =>
                    CompleteEndedFailure(recordingContext, ex)
                );
                ClearActiveState();
                return;
            }

            ClearActiveState();
            DeleteTempFile(tempPath, operation.RecordingId);
            DeleteWavBestEffort(wavPaths);
            operations.TryComplete(
                operation,
                new ReplayVideoRecordingCompletion
                {
                    FinalFilePath = finalPath,
                    AudioStatus = ReplayVideoAudioStatus.Failed,
                    MetadataStatus = ReplayVideoMetadataStatus.Failed,
                    ReasonCode = ReplayVideoRecordingReasonCode.CaptureFailed,
                    Exception = ex,
                }
            );
        }
    }

    private static void CompleteEndedRecording(EndedRecordingFinalizeContext context)
    {
        var recording = context.Recording;
        ReplayVideoCaptureResult? result = null;
        try
        {
            result = recording.Drain.Complete();
            var mux = context.Muxer.Resolve(
                recording.Operation.RecordingId,
                result.Status,
                recording.TempVideoPath,
                recording.FinalPath,
                recording.WavPaths ?? Array.Empty<string>(),
                context.FfmpegExecutable
            );
            try
            {
                var resolvedMetadata = TrySaveFinishMetadataFor(
                    context.Store,
                    context.VideoDir,
                    mux.FinalFilePath,
                    result,
                    mux.Status != ReplayVideoAudioMuxer.MuxStatus.Failed,
                    mux.FileSizeBytes,
                    context.MetadataStatus
                );
                recording.Operations.CompleteResolved(
                    recording.Operation,
                    result,
                    mux,
                    context.AudioStatus,
                    resolvedMetadata.Status,
                    context.DegradationReason,
                    context.DegradationException ?? resolvedMetadata.Exception
                );
            }
            catch (Exception ex)
            {
                recording.Operations.CompleteMuxCallbackFailure(
                    recording.Operation,
                    result,
                    mux,
                    context.AudioStatus,
                    context.MetadataStatus,
                    ex
                );
            }
        }
        catch (Exception ex)
        {
            CompleteFailedRecording(recording, result, ex);
        }
    }

    private static void CompleteEndedFailure(
        RecordingFinalizeContext recording,
        Exception exception
    )
    {
        ReplayVideoCaptureResult? result = null;
        try
        {
            result = recording.Drain.Complete();
        }
        catch (Exception drainException)
        {
            exception = new AggregateException(exception, drainException);
        }

        CompleteFailedRecording(recording, result, exception);
    }

    private static void CompleteFailedRecording(
        RecordingFinalizeContext recording,
        ReplayVideoCaptureResult? result,
        Exception exception
    )
    {
        DeleteTempFile(recording.TempVideoPath, recording.Operation.RecordingId);
        DeleteWavBestEffort(recording.WavPaths);
        recording.Operations.TryComplete(
            recording.Operation,
            new ReplayVideoRecordingCompletion
            {
                FinalFilePath = recording.FinalPath,
                CapturedFrames = result?.CapturedFrames ?? 0,
                DroppedFrames = result?.DroppedFrames ?? 0,
                AudioStatus = ReplayVideoAudioStatus.Failed,
                MetadataStatus = ReplayVideoMetadataStatus.Failed,
                ReasonCode =
                    result?.ReasonCode is { } resultReason
                    && resultReason != ReplayVideoRecordingReasonCode.Completed
                        ? resultReason
                        : ReplayVideoRecordingReasonCode.CaptureFailed,
                ExitCode = result?.ExitCode,
                StderrTail = result?.StderrTail,
                Exception = exception,
                EndedAtUtc = result?.EndedAtUtc,
            }
        );
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

    private void CompletePreflightFailure(
        ReplayVideoRecordingOperation operation,
        ReplayVideoRecordingReasonCode reasonCode
    )
    {
        _operations.CompletePreflight(operation, reasonCode);
    }

    private static ReplayVideoRecordingReasonCode MapGateBlocker(
        CombatReplayRecordingBlocker blocker
    ) =>
        blocker switch
        {
            CombatReplayRecordingBlocker.NoAsyncGpuReadback =>
                ReplayVideoRecordingReasonCode.AsyncGpuReadbackUnavailable,
            CombatReplayRecordingBlocker.FfmpegUnavailable =>
                ReplayVideoRecordingReasonCode.FfmpegUnavailable,
            CombatReplayRecordingBlocker.VideoDirectoryUnset =>
                ReplayVideoRecordingReasonCode.OutputPathUnavailable,
            _ => ReplayVideoRecordingReasonCode.CaptureFailed,
        };

    private void ApplyAudioStopOutcomes(
        IReadOnlyList<ReplayAudioCaptureResult> results,
        bool hasUsableAudio
    )
    {
        ReplayAudioCaptureResult? firstFailure = null;
        for (var index = 0; index < results.Count; index++)
        {
            if (results[index].FailureReason != ReplayAudioFailureReasonCode.None)
            {
                firstFailure = results[index];
                break;
            }
        }

        if (firstFailure.HasValue)
        {
            _activeAudioStatus = ReplayVideoAudioStatus.Failed;
            _activeDegradationReason ??= ReplayVideoRecordingReasonCode.AudioStopFailed;
            _activeDegradationException ??= firstFailure.Value.FailureException;
            return;
        }

        _activeAudioStatus = hasUsableAudio
            ? ReplayVideoAudioStatus.Full
            : ReplayVideoAudioStatus.Silent;
        if (!hasUsableAudio)
            _activeDegradationReason ??= ReplayVideoRecordingReasonCode.AudioUnavailable;
    }

    private void ClearActiveState()
    {
        _activeSession = null;
        _activeOperation = null;
        _captureCoroutine = null;
        _activeRecordingTempPath = null;
        _activeRecordingFinalPath = null;
        _activeAudioWavPaths = null;
        _activeFfmpegExecutable = null;
        _activeAudioStatus = ReplayVideoAudioStatus.Silent;
        _activeMetadataStatus = ReplayVideoMetadataStatus.Unavailable;
        _activeDegradationReason = null;
        _activeDegradationException = null;
    }

    private void BeginRecording(
        ReplayVideoRecordingOperation operation,
        ReplayVideoCaptureRequest request,
        IBppServices services
    )
    {
        var session = new ReplayVideoCaptureSession(request);
        _activeOperation = operation;
        _activeSession = session;
        _activeRecordingTempPath = request.OutputFilePath;
        _activeRecordingFinalPath = request.FinalOutputFilePath;
        _activeFfmpegExecutable = request.FfmpegExecutable;
        _activeAudioStatus = ReplayVideoAudioStatus.Silent;
        _activeMetadataStatus = ReplayVideoMetadataStatus.Unavailable;
        _activeDegradationReason = null;
        _activeDegradationException = null;
        try
        {
            session.Start();
        }
        catch
        {
            try
            {
                session.Dispose();
            }
            catch { }
            throw;
        }

        StartAudioTaps(operation, request.OutputFilePath);

        _uiSuppressionScope = BeginUiSuppression(operation.RecordingId);

        _activeMetadataStatus =
            operation.Source == CombatReplayPlaybackSource.CurrentNative
                ? ReplayVideoMetadataStatus.Complete
                : TrySaveStartMetadata(request, services);
        if (_activeMetadataStatus != ReplayVideoMetadataStatus.Complete)
        {
            _activeDegradationReason ??= ReplayVideoRecordingReasonCode.MetadataFailed;
            _activeDegradationException ??= _metadataInitializationException;
        }

        _captureCoroutine = StartCoroutine(CaptureLoop(session));

        BppLog.DebugEvent(
            CombatReplayVideoLogEvents.RecordingLifecycleObserved,
            () =>
                [
                    CombatReplayVideoLogEvents.LifecycleStage.Bind(
                        ReplayVideoLogStage.SessionStarted
                    ),
                    CombatReplayVideoLogEvents.LifecycleRecordingId.Bind(operation.RecordingId),
                    CombatReplayVideoLogEvents.LifecycleBattleId.Bind(operation.BattleId),
                    CombatReplayVideoLogEvents.LifecyclePendingCount.Bind(_operations.Count),
                ]
        );
    }

    private void StartAudioTaps(ReplayVideoRecordingOperation operation, string tempVideoPath)
    {
        // Audio is additive: capture failure must never abort the video recording. Capture the device
        // output (loopback) so we record exactly what the player hears — music, settlement, and the
        // spatialised combat/board SFX that no FMOD channel group exposes.
        var wavPath = ReplayVideoAudioTapPlan.DeriveAudioWavPath(tempVideoPath);
        _activeAudioWavPaths = new List<string> { wavPath };
        TryStartAudioTap(operation, wavPath);

        if (_audioTaps.Count == 0)
        {
            DeleteWavBestEffort(_activeAudioWavPaths);
            _activeAudioWavPaths = null;
        }
    }

    private void TryStartAudioTap(ReplayVideoRecordingOperation operation, string wavPath)
    {
        try
        {
            IReplayAudioCaptureTap tap = ReplayAudioCaptureFactory.Create(wavPath);
            var outcome = tap.TryStart();
            if (outcome.Started)
            {
                _audioTaps.Add(tap);
                _activeAudioStatus = ReplayVideoAudioStatus.Full;
                BppLog.DebugEvent(
                    CombatReplayVideoLogEvents.AudioCaptureStarted,
                    () =>
                        [
                            CombatReplayVideoLogEvents.AudioStartedRecordingId.Bind(
                                operation.RecordingId
                            ),
                            CombatReplayVideoLogEvents.AudioStartedBackend.Bind(tap.Backend),
                            CombatReplayVideoLogEvents.AudioStartedSampleRate.Bind(
                                tap.SampleRateHz
                            ),
                            CombatReplayVideoLogEvents.AudioStartedChannels.Bind(tap.Channels),
                            CombatReplayVideoLogEvents.AudioStartedSampleFormat.Bind(
                                tap.SampleFormat
                            ),
                        ]
                );
                return;
            }

            _activeAudioStatus = ReplayVideoAudioStatus.Silent;
            _activeDegradationReason =
                outcome.ReasonCode == ReplayAudioFailureReasonCode.UnsupportedPlatform
                    ? ReplayVideoRecordingReasonCode.AudioUnavailable
                    : ReplayVideoRecordingReasonCode.AudioCaptureFailed;
            _activeDegradationException = outcome.Exception;
            tap.Dispose();
            DeleteWavBestEffort(wavPath);
        }
        catch (Exception ex)
        {
            _activeAudioStatus = ReplayVideoAudioStatus.Silent;
            _activeDegradationReason = ReplayVideoRecordingReasonCode.AudioCaptureFailed;
            _activeDegradationException = ex;
            DeleteWavBestEffort(wavPath);
        }
    }

    private ReplayVideoMetadataStatus TrySaveStartMetadata(
        ReplayVideoCaptureRequest request,
        IBppServices services
    )
    {
        var store = _metadataStore;
        if (store == null)
            return ReplayVideoMetadataStatus.Unavailable;

        try
        {
            store.SaveStart(CreateStartedMetadata(request, services));
            return ReplayVideoMetadataStatus.Complete;
        }
        catch (Exception ex)
        {
            _activeDegradationException = ex;
            return ReplayVideoMetadataStatus.Failed;
        }
    }

    private static VideoRecordingStarted CreateStartedMetadata(
        ReplayVideoCaptureRequest request,
        IBppServices services
    ) =>
        new()
        {
            VideoId = request.VideoId,
            BattleId = request.BattleId,
            Source = request.Source.ToString(),
            VideoRelativePath = ComputeRelativePath(
                services.Paths.CombatReplayVideoDirectoryPath,
                request.FinalOutputFilePath
            ),
            Width = request.Width,
            Height = request.Height,
            Fps = request.Fps,
            Codec = request.EncoderProfile.Codec,
            Crf = request.EncoderProfile.Crf,
            Preset = request.EncoderProfile.Preset,
            StartedAtUtc = DateTimeOffset.UtcNow,
        };

    // Parameterized so it is race-free under the async mux: every input is a value
    // captured on the main thread before instance fields are nulled. Safe to call on
    // a background thread because the store opens a fresh SQLite connection per call.
    // FileSizeBytes always comes from the resolved final path, never the first-pass temp.
    private static MetadataWriteOutcome TrySaveFinishMetadataFor(
        CombatReplayVideoMetadataStore? store,
        string? videoDir,
        string finalPath,
        ReplayVideoCaptureResult result,
        bool finalResolutionSucceeded,
        long finalFileSize,
        ReplayVideoMetadataStatus initialStatus
    )
    {
        if (store == null)
            return new MetadataWriteOutcome(initialStatus, null);

        try
        {
            var relativePath = ComputeRelativePath(videoDir, finalPath);
            var endedAt = result.EndedAtUtc ?? DateTimeOffset.UtcNow;
            var status = ReplayVideoMetadataResolution.ResolvePersistedStatus(
                result.Status,
                finalResolutionSucceeded,
                finalFileSize
            );

            store.SaveFinish(
                new VideoRecordingFinished
                {
                    VideoId = result.VideoId,
                    VideoRelativePath = relativePath,
                    EndedAtUtc = endedAt,
                    DurationMs = result.DurationMs,
                    CapturedFrames = result.CapturedFrames,
                    DroppedFrames = result.DroppedFrames,
                    FileSizeBytes = finalFileSize,
                    Status = status,
                    Error = result.Error,
                }
            );
            return new MetadataWriteOutcome(
                initialStatus == ReplayVideoMetadataStatus.Complete
                    ? ReplayVideoMetadataStatus.Complete
                    : ReplayVideoMetadataStatus.Failed,
                null
            );
        }
        catch (Exception ex)
        {
            return new MetadataWriteOutcome(ReplayVideoMetadataStatus.Failed, ex);
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
        var operation = _activeOperation;
        var terminalReason = reason switch
        {
            "superseded" => ReplayVideoRecordingReasonCode.Superseded,
            "begin-exception" => ReplayVideoRecordingReasonCode.BeginException,
            _ => ReplayVideoRecordingReasonCode.Aborted,
        };
        var terminalException = _activeDegradationException;

        StopCaptureCoroutine();
        // Tear the tap down synchronously: stop + join before any new recording's
        // capture thread starts (covers superseded, OnDisable, OnDestroy, and the
        // scene-change-driven OnDisable). Abort never muxes — it deletes temps.
        var wavPaths = _activeAudioWavPaths;
        if (operation != null)
        {
            var usable = ReplayAudioTapStopper.StopAndCollectUsableWavPaths(
                _audioTaps,
                operation.RecordingId,
                out var audioResults
            );
            ApplyAudioStopOutcomes(audioResults, usable.Count > 0);
        }

        // Freeze all state before dispatch. ClearActiveState runs before this method returns, and a
        // superseding recording is then free to overwrite every _active* field.
        var tempPath = _activeRecordingTempPath ?? session?.Request.OutputFilePath ?? string.Empty;
        var finalPath =
            _activeRecordingFinalPath
            ?? (session == null ? string.Empty : session.Request.FinalOutputFilePath);
        var store = _metadataStore;
        var videoDir = _services?.Paths.CombatReplayVideoDirectoryPath;
        var audioStatus = _activeAudioStatus;
        var metadataStatus = _activeMetadataStatus;
        var operations = _operations;
        ReplayVideoEncoderDrain? drain = null;
        if (session != null)
        {
            try
            {
                drain = session.Finalize(reason);
            }
            catch (Exception ex)
            {
                terminalReason = ReplayVideoRecordingReasonCode.CaptureFailed;
                terminalException = ex;
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
        if (operation == null)
        {
            ClearActiveState();
            DeleteTempFile(tempPath, recordingId: null);
            DeleteWavBestEffort(wavPaths);
            return;
        }

        if (drain != null)
        {
            var recordingContext = new RecordingFinalizeContext(
                operations,
                operation,
                drain,
                tempPath,
                finalPath,
                wavPaths
            );
            var abortedContext = new AbortedRecordingFinalizeContext(
                recordingContext,
                reason,
                store,
                videoDir,
                audioStatus,
                metadataStatus,
                terminalReason,
                terminalException
            );
            ReplayVideoAudioMuxer.DispatchTracked(() => CompleteAbortedRecording(abortedContext));
            ClearActiveState();
            return;
        }

        ClearActiveState();
        DeleteTempFile(tempPath, operation.RecordingId);
        DeleteWavBestEffort(wavPaths);
        operations.TryComplete(
            operation,
            new ReplayVideoRecordingCompletion
            {
                FinalFilePath = finalPath,
                AudioStatus = audioStatus,
                MetadataStatus = metadataStatus,
                ReasonCode = terminalReason,
                Exception = terminalException,
            }
        );
    }

    private static void CompleteAbortedRecording(AbortedRecordingFinalizeContext context)
    {
        var recording = context.Recording;
        var terminalReason = context.TerminalReason;
        var terminalException = context.TerminalException;
        var metadataStatus = context.MetadataStatus;
        ReplayVideoCaptureResult? result = null;
        var finalResolutionSucceeded = false;
        try
        {
            result = recording.Drain.Complete();
            if (result.Status == ReplayVideoCaptureStatus.Completed)
            {
                try
                {
                    ReplayVideoAudioMuxer.PromoteSilentToFinal(
                        recording.TempVideoPath,
                        recording.FinalPath
                    );
                    finalResolutionSucceeded = true;
                }
                catch (Exception ex)
                {
                    terminalReason = ReplayVideoRecordingReasonCode.PromotionFailed;
                    terminalException = ex;
                }
            }
            else if (!string.Equals(context.EndReason, "begin-exception", StringComparison.Ordinal))
            {
                terminalReason = result.ReasonCode;
            }

            var finalFileSize = FfmpegRawVideoEncoder.TryGetFileSize(recording.FinalPath);
            var metadataOutcome = TrySaveFinishMetadataFor(
                context.Store,
                context.VideoDir,
                recording.FinalPath,
                result,
                finalResolutionSucceeded,
                finalFileSize,
                metadataStatus
            );
            metadataStatus = metadataOutcome.Status;
            terminalException ??= metadataOutcome.Exception;
        }
        catch (Exception ex)
        {
            terminalReason = ReplayVideoRecordingReasonCode.CaptureFailed;
            terminalException = ex;
        }

        DeleteTempFile(recording.TempVideoPath, recording.Operation.RecordingId);
        DeleteWavBestEffort(recording.WavPaths);
        recording.Operations.TryComplete(
            recording.Operation,
            new ReplayVideoRecordingCompletion
            {
                FinalFilePath = recording.FinalPath,
                CapturedFrames = result?.CapturedFrames ?? 0,
                DroppedFrames = result?.DroppedFrames ?? 0,
                AudioStatus = context.AudioStatus,
                MetadataStatus = metadataStatus,
                ReasonCode = terminalReason,
                ExitCode = result?.ExitCode,
                StderrTail = result?.StderrTail,
                Exception = terminalException ?? result?.Exception,
                EndedAtUtc = result?.EndedAtUtc,
            }
        );
    }

    private static IDisposable? BeginUiSuppression(string recordingId)
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
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.RecordingCleanupFailed,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.CleanupRecordingId.Bind(recordingId),
                        CombatReplayVideoLogEvents.CleanupStage.Bind(
                            ReplayVideoLogStage.UiSuppression
                        ),
                        CombatReplayVideoLogEvents.CleanupPath.Bind(null),
                    ]
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
        string recordingId,
        CombatReplayPlaybackStarting evt,
        string ffmpegExecutable,
        string videoDirectoryPath,
        ReplayVideoCaptureSettings? preparedSettings = null
    )
    {
        ReplayVideoCaptureSettings captureSettings;
        if (preparedSettings.HasValue)
        {
            captureSettings = preparedSettings.Value;
        }
        else if (!ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out captureSettings))
        {
            return null;
        }
        var fps = captureSettings.Fps;
        var width = captureSettings.Width;
        var height = captureSettings.Height;

        ReplayVideoBufferPlan bufferPlan;
        try
        {
            bufferPlan = ReplayVideoBufferPlan.Create(width, height);
        }
        catch
        {
            return null;
        }

        var encoderProfile = FfmpegVideoEncoderSelector.SelectOrPrewarm(
            ffmpegExecutable,
            videoDirectoryPath,
            width,
            height,
            fps
        );

        var nowLocal = DateTimeOffset.Now;
        var datePart = nowLocal.ToString("yyyy-MM-dd");
        var stampPart = nowLocal.ToString("yyyyMMdd-HHmmss");
        var sanitizedBattleId = SanitizeForPath(evt.BattleId);
        var outputDirectory = Path.Combine(videoDirectoryPath, datePart);
        var fileNames = ReplayVideoOutputFileNames.Create(
            sanitizedBattleId,
            stampPart,
            recordingId
        );
        var outputFilePath = Path.Combine(outputDirectory, fileNames.TempFileName);
        var finalOutputFilePath = Path.Combine(outputDirectory, fileNames.FinalFileName);

        return new ReplayVideoCaptureRequest
        {
            VideoId = recordingId,
            BattleId = evt.BattleId,
            Source = evt.Source,
            FfmpegExecutable = ffmpegExecutable,
            OutputFilePath = outputFilePath,
            FinalOutputFilePath = finalOutputFilePath,
            OutputDirectoryPath = outputDirectory,
            Width = width,
            Height = height,
            Fps = fps,
            EncoderProfile = encoderProfile,
            BufferPlan = bufferPlan,
        };
    }

    private static void DeleteTempFile(string? tempPath, string? recordingId)
    {
        if (string.IsNullOrWhiteSpace(tempPath))
            return;

        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                CombatReplayVideoLogEvents.RecordingCleanupFailed,
                ex,
                () =>
                    [
                        CombatReplayVideoLogEvents.CleanupRecordingId.Bind(recordingId),
                        CombatReplayVideoLogEvents.CleanupStage.Bind(
                            ReplayVideoLogStage.TempDelete
                        ),
                        CombatReplayVideoLogEvents.CleanupPath.Bind(tempPath),
                    ]
            );
        }
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

    private CurrentReplayRecorderAvailability SetAvailability(
        CurrentReplayRecorderAvailabilityPhase phase,
        string? reason
    )
    {
        lock (_availabilitySync)
        {
            _availabilityGeneration++;
            _currentReplayAvailability = new CurrentReplayRecorderAvailability(phase, reason);
            if (phase != CurrentReplayRecorderAvailabilityPhase.Ready)
                _availabilityFfmpegExecutable = null;
            return _currentReplayAvailability;
        }
    }

    private void SetAvailabilityIfCurrent(
        int generation,
        CurrentReplayRecorderAvailabilityPhase phase,
        string? reason
    )
    {
        lock (_availabilitySync)
        {
            if (generation != _availabilityGeneration)
                return;
            _currentReplayAvailability = new CurrentReplayRecorderAvailability(phase, reason);
            if (phase != CurrentReplayRecorderAvailabilityPhase.Ready)
                _availabilityFfmpegExecutable = null;
        }
    }

    private void CancelPreparedCurrentReplay(ReplayVideoRecordingReasonCode reasonCode)
    {
        var prepared = _preparedCurrentReplay;
        _preparedCurrentReplay = null;
        if (prepared != null)
        {
            TryMarkPreparedMetadataFailed(prepared, reasonCode.ToString());
            _operations.CompletePreflight(prepared.Operation, reasonCode);
        }
    }

    private void TryMarkPreparedMetadataFailed(
        PreparedCurrentReplayRecording prepared,
        string error
    )
    {
        var store = _metadataStore;
        if (store == null)
            return;

        try
        {
            store.SaveFinish(
                new VideoRecordingFinished
                {
                    VideoId = prepared.Operation.RecordingId,
                    VideoRelativePath = ComputeRelativePath(
                        _services?.Paths.CombatReplayVideoDirectoryPath,
                        prepared.Request.FinalOutputFilePath
                    ),
                    EndedAtUtc = DateTimeOffset.UtcNow,
                    DurationMs = 0,
                    CapturedFrames = 0,
                    DroppedFrames = 0,
                    FileSizeBytes = null,
                    Status = "FAILED",
                    Error = error,
                }
            );
        }
        catch
        {
            // Best effort: the terminal observer still reports the reservation failure.
        }
    }

    private void OnOperationCompleted(ReplayVideoRecordingTerminal terminal)
    {
        _completionEvents.Enqueue(
            new CombatReplayVideoRecordingCompleted
            {
                RecordingId = terminal.RecordingId,
                BattleId = terminal.BattleId,
                Source = terminal.Source,
                FinalFilePath = terminal.FinalFilePath,
                ArtifactUsable = terminal.ArtifactUsable,
                AudioStatus = terminal.AudioStatus,
                MetadataStatus = terminal.MetadataStatus,
                ReasonCode = terminal.ReasonCode,
                Reason =
                    terminal.Reason
                    ?? (
                        terminal.ReasonCode == ReplayVideoRecordingReasonCode.Completed
                            ? null
                            : terminal.ReasonCode.ToString()
                    ),
            }
        );
    }

    private readonly struct MetadataWriteOutcome
    {
        internal MetadataWriteOutcome(ReplayVideoMetadataStatus status, Exception? exception)
        {
            Status = status;
            Exception = exception;
        }

        internal ReplayVideoMetadataStatus Status { get; }
        internal Exception? Exception { get; }
    }

    private sealed record RecordingFinalizeContext(
        ReplayVideoRecordingLifecycle Operations,
        ReplayVideoRecordingOperation Operation,
        ReplayVideoEncoderDrain Drain,
        string TempVideoPath,
        string FinalPath,
        IReadOnlyList<string>? WavPaths
    );

    private sealed record PreparedCurrentReplayRecording(
        ReplayVideoRecordingOperation Operation,
        ReplayVideoCaptureRequest Request
    );

    private sealed record EndedRecordingFinalizeContext(
        RecordingFinalizeContext Recording,
        ReplayVideoAudioMuxer Muxer,
        string? FfmpegExecutable,
        CombatReplayVideoMetadataStore? Store,
        string? VideoDir,
        ReplayVideoAudioStatus AudioStatus,
        ReplayVideoMetadataStatus MetadataStatus,
        ReplayVideoRecordingReasonCode? DegradationReason,
        Exception? DegradationException
    );

    private sealed record AbortedRecordingFinalizeContext(
        RecordingFinalizeContext Recording,
        string EndReason,
        CombatReplayVideoMetadataStore? Store,
        string? VideoDir,
        ReplayVideoAudioStatus AudioStatus,
        ReplayVideoMetadataStatus MetadataStatus,
        ReplayVideoRecordingReasonCode TerminalReason,
        Exception? TerminalException
    );
}
