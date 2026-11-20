#nullable enable
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Audio;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.GameInterop.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.Paths;
using UnityEngine;
using static BazaarPlusPlus.Game.CombatReplay.Video.ReplayVideoRecordingFinalizer;

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
    private ReplayVideoRecordingLifecycle _operations = null!;
    private ReplayVideoRecordingOperation? _activeOperation;
    private PreparedCurrentReplayRecording? _preparedCurrentReplay;
    private readonly ConcurrentQueue<CombatReplayVideoRecordingCompleted> _completionEvents = new();
    private readonly ConcurrentDictionary<string, IDisposable> _artifactProtectionLeases = new();
    private ReplayMaintenanceFlight? _videoMaintenanceFlight;
    private readonly object _availabilitySync = new();
    private CurrentReplayRecorderAvailability _currentReplayAvailability = new(
        CurrentReplayRecorderAvailabilityPhase.Unavailable,
        "Video recorder is unavailable."
    );
    private ReplayVideoCaptureSettings _availabilitySettings;
    private string? _availabilityVideoDirectory;

    // SaveFinish updates an existing row, so finalization waits for the asynchronous History insert.
    private Task<MetadataWriteOutcome>? _activeStartMetadata;

    private Task<ReplayAudioStopOutcome>? _pendingAudioStop;
    private ReplayVideoRecordingReasonCode? _activeDegradationReason;
    private Exception? _activeDegradationException;
    private Exception? _metadataInitializationException;
    private ReplayVideoCopyTimingAccumulator? _playbackFrameTiming;
    private string? _playbackTimingBattleId;
    private bool _playbackTimingRecorded;

    private void Awake()
    {
        _operations = new ReplayVideoRecordingLifecycle(null, OnOperationCompleted);
    }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out _);

        var runLogDatabasePath = PathConstants.RunLogDatabase(services.Paths.RequireDataRoot());
        if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
        {
            try
            {
                _metadataStore = new CombatReplayVideoMetadataStore(runLogDatabasePath);
                StartVideoMaintenance(
                    _metadataStore,
                    PathConstants.CombatReplayVideos(services.Paths.RequireDataRoot())
                );
            }
            catch (Exception ex)
            {
                _metadataInitializationException = ex;
                _metadataStore = null;
            }
        }

        // AddComponent fires OnEnable synchronously on an active host, before this initializer supplies the services.
        if (isActiveAndEnabled)
            EnsureEventSubscriptions();
    }

    private void Update()
    {
        _playbackFrameTiming?.ObserveMicroseconds(
            (long)Math.Round(Math.Max(0, Time.unscaledDeltaTime) * 1_000_000d)
        );

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
            return SetAvailability(
                CurrentReplayRecorderAvailabilityPhase.Unavailable,
                "Video database is unavailable."
            );

        var backend = ReplayVideoBackendPolicy.Current;
        if (backend == ReplayVideoBackend.Unsupported)
            return SetAvailability(
                CurrentReplayRecorderAvailabilityPhase.Unavailable,
                "Video recording is supported on macOS and Windows."
            );

        var videoDirectory = VideoDirectory(services);
        if (string.IsNullOrWhiteSpace(videoDirectory))
            return SetAvailability(
                CurrentReplayRecorderAvailabilityPhase.Unavailable,
                "Video output directory is unavailable."
            );

        if (!ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out var settings))
            return SetAvailability(
                CurrentReplayRecorderAvailabilityPhase.Unavailable,
                "The current game resolution cannot be recorded."
            );

        var available =
            backend == ReplayVideoBackend.MacNative
                ? MacMetalVideoEncoder.TryGetAvailability(out var nativeReason)
                : WindowsMediaFoundationVideoEncoder.TryGetAvailability(out nativeReason);
        if (!available)
            return SetAvailability(
                CurrentReplayRecorderAvailabilityPhase.Unavailable,
                nativeReason ?? "The native video recorder is unavailable."
            );

        lock (_availabilitySync)
        {
            _availabilitySettings = settings;
            _availabilityVideoDirectory = videoDirectory;
            _currentReplayAvailability = new CurrentReplayRecorderAvailability(
                CurrentReplayRecorderAvailabilityPhase.Ready,
                null
            );
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
        string? videoDirectory;
        CurrentReplayRecorderAvailability availability;
        lock (_availabilitySync)
        {
            availability = _currentReplayAvailability;
            settings = _availabilitySettings;
            videoDirectory = _availabilityVideoDirectory;
        }

        if (!availability.IsReady || string.IsNullOrWhiteSpace(videoDirectory))
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
            var request = BuildCaptureRequest(operation.RecordingId, evt, videoDirectory, settings);
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
                _metadataStore.ReserveStartOnMainThread(CreateStartedMetadata(request, services));
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
        _videoMaintenanceFlight?.Dispose();
        _videoMaintenanceFlight = null;
        AbortActiveSession("recorder-destroyed");

        // This lifecycle hook gives pending finalizers a bounded shutdown window. The next launch reclaims unfinished temp files.
        try
        {
            if (!ReplayVideoAudioMuxer.TryDrainPendingForShutdown(TimeSpan.FromMilliseconds(4000)))
            {
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatReplay,
                        "combat_replay.video_recording.lifecycle_observed"
                    ),
                    () =>
                        [
                            ("stage", ReplayVideoLogStage.MuxDrain),
                            ("recording_id", null, BppLogCorrelationPolicy.Short),
                            ("battle_id", null, BppLogCorrelationPolicy.Short),
                            ("pending_count", _operations.Count),
                        ]
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.video_mux.diagnostic_observed"
                ),
                ex,
                () =>
                    [
                        ("recording_id", null, BppLogCorrelationPolicy.Short),
                        ("stage", ReplayVideoLogStage.MuxDrain),
                        ("reason_code", ReplayVideoDiagnosticReasonCode.DrainFailed),
                        ("path", null),
                        ("pending_count", _operations.Count),
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

        _playbackFrameTiming = new ReplayVideoCopyTimingAccumulator();
        _playbackTimingBattleId = evt.BattleId;
        _playbackTimingRecorded = false;

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

            var gate = CombatReplayRecordingGate.Evaluate(VideoDirectory(services));
            if (!gate.CanRecord)
            {
                CompletePreflightFailure(operation, MapGateBlocker(gate.Blocker));
                return;
            }

            var request = BuildCaptureRequest(operation.RecordingId, evt, gate.VideoDirectoryPath!);
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
            var aborted = AbortActiveSession("begin-exception");
            CompleteBeginException(_operations, operation, aborted, ex);
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
            // The provisional FAILED write must precede the finalizer, which can still find a usable MP4.
            var failedMark = TryMarkPreparedMetadataFailed(prepared, ex.Message);
            var aborted = AbortActiveSession("begin-exception", metadataWriteBefore: failedMark);
            CompleteBeginException(_operations, prepared.Operation, aborted, ex);
        }
    }

    private void OnPlaybackEnded(CombatReplayPlaybackEnded evt)
    {
        LogPlaybackFrameTiming();
        if (evt == null || _activeSession == null || _activeOperation == null)
            return;

        var session = _activeSession;
        var operation = _activeOperation;
        var reason = evt.Reason ?? (evt.Failed ? "playback-failed" : "playback-ended");
        ReplayVideoEncoderDrain? drain = null;
        Task<ReplayAudioStopOutcome>? audioStop = null;

        try
        {
            StopCaptureCoroutine();
            drain = session.Finalize(reason);
            audioStop = StopAudioTapsOffMainThread(operation.RecordingId);

            // Freeze every continuation input before clearing the active recording. A superseding
            // replay may replace all _active* fields as soon as this method returns.
            var tempVideoPath = _activeRecordingTempPath ?? session.Request.OutputFilePath;
            var finalPath = _activeRecordingFinalPath ?? session.Request.FinalOutputFilePath;
            var videoDir = _services == null ? null : VideoDirectory(_services);
            var store = _metadataStore;
            var startInputs = FreezeStartInputs();
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
                _activeAudioWavPaths,
                audioStop
            );
            var endedContext = new EndedRecordingFinalizeContext(
                recordingContext,
                muxer,
                store,
                videoDir,
                startInputs
            );
            ReplayVideoAudioMuxer.DispatchTracked(() => CompleteEndedRecording(endedContext));
            ClearActiveState();
        }
        catch (Exception ex)
        {
            var wavPaths = _activeAudioWavPaths;
            audioStop ??= StopAudioTapsOffMainThread(operation.RecordingId);
            var tempPath = _activeRecordingTempPath ?? session.Request.OutputFilePath;
            var finalPath = _activeRecordingFinalPath ?? session.Request.FinalOutputFilePath;
            DisposeUiState();
            session.Dispose();
            var operations = _operations;
            ClearActiveState();
            if (drain != null)
            {
                var recordingContext = new RecordingFinalizeContext(
                    operations,
                    operation,
                    drain,
                    tempPath,
                    finalPath,
                    wavPaths,
                    audioStop
                );
                ReplayVideoAudioMuxer.DispatchTracked(() =>
                    CompleteEndedFailure(recordingContext, ex)
                );
                return;
            }

            var stop = audioStop;
            ReplayVideoAudioMuxer.DispatchTracked(() =>
            {
                WaitForAudioStop(stop);
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
            });
        }
    }

    private void LogPlaybackFrameTiming()
    {
        var timing = _playbackFrameTiming;
        if (timing == null)
            return;

        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.video_capture.native_pipeline_observed"
            ),
            () =>
                [
                    ("battle_id", _playbackTimingBattleId, BppLogCorrelationPolicy.Short),
                    (
                        "stage",
                        _playbackTimingRecorded ? "playback_recorded" : "playback_unrecorded"
                    ),
                    ("render_frame_p50_us", timing.P50Microseconds),
                    ("render_frame_p95_us", timing.P95Microseconds),
                    ("render_frame_p99_us", timing.P99Microseconds),
                ]
        );
        _playbackFrameTiming = null;
        _playbackTimingBattleId = null;
        _playbackTimingRecorded = false;
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
            CombatReplayRecordingBlocker.UnsupportedPlatform =>
                ReplayVideoRecordingReasonCode.UnsupportedPlatform,
            CombatReplayRecordingBlocker.NativeRecorderUnavailable =>
                ReplayVideoRecordingReasonCode.NativeRecorderUnavailable,
            CombatReplayRecordingBlocker.VideoDirectoryUnset =>
                ReplayVideoRecordingReasonCode.OutputPathUnavailable,
            _ => ReplayVideoRecordingReasonCode.CaptureFailed,
        };

    private void ClearActiveState()
    {
        _activeSession = null;
        _activeOperation = null;
        _captureCoroutine = null;
        _activeRecordingTempPath = null;
        _activeRecordingFinalPath = null;
        _activeAudioWavPaths = null;
        _activeStartMetadata = null;
        _activeDegradationReason = null;
        _activeDegradationException = null;
    }

    // Capture on the main thread before a superseding recording can replace the active state.
    private RecordingStartInputs FreezeStartInputs() =>
        new(
            _activeStartMetadata ?? Task.FromResult(MetadataWriteOutcome.Unavailable),
            _activeDegradationReason,
            _activeDegradationException
        );

    private Task<ReplayAudioStopOutcome> StopAudioTapsOffMainThread(string recordingId)
    {
        var taps = ReplayAudioTapStopper.Detach(_audioTaps);
        if (taps.Count == 0)
            return Task.FromResult(ReplayAudioStopOutcome.Empty);

        var stop = ReplayVideoAudioMuxer.DispatchTracked(() =>
            ReplayAudioTapStopper.Stop(taps, recordingId)
        );
        _pendingAudioStop = stop;
        return stop;
    }

    // The macOS aggregate device has a fixed UID, so the next tap must wait for the previous teardown. WASAPI has no shared device to release.
    private void WaitForPreviousAudioStop()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _pendingAudioStop = null;
            return;
        }

        var pending = _pendingAudioStop;
        if (pending == null)
            return;
        if (!pending.IsCompleted)
        {
            try
            {
                pending.Wait(ReplayAudioCaptureThreadJoiner.StopTimeoutMs + 500);
            }
            catch { }
        }
        if (pending.IsCompleted)
            _pendingAudioStop = null;
    }

    private void BeginRecording(
        ReplayVideoRecordingOperation operation,
        ReplayVideoCaptureRequest request,
        IBppServices services
    )
    {
        var protection = ReplayVideoInFlightArtifacts.Protect(
            operation.RecordingId,
            [
                request.OutputFilePath,
                request.FinalOutputFilePath,
                ReplayVideoAudioTapPlan.DeriveAudioWavPath(request.OutputFilePath),
            ]
        );
        if (!_artifactProtectionLeases.TryAdd(operation.RecordingId, protection))
            protection.Dispose();
        WaitForPreviousAudioStop();
        var session = new ReplayVideoCaptureSession(request);
        _activeOperation = operation;
        _activeSession = session;
        _activeRecordingTempPath = request.OutputFilePath;
        _activeRecordingFinalPath = request.FinalOutputFilePath;
        _activeStartMetadata = null;
        _activeDegradationReason = null;
        _activeDegradationException = null;
        try
        {
            session.Start();
            _playbackTimingRecorded = true;
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

        _activeStartMetadata =
            operation.Source == CombatReplayPlaybackSource.CurrentNative
                ? Task.FromResult(MetadataWriteOutcome.Complete)
                : SaveStartMetadataOffMainThread(request, services);

        _captureCoroutine = StartCoroutine(CaptureLoop(session));

        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.video_recording.lifecycle_observed"
            ),
            () =>
                [
                    ("stage", ReplayVideoLogStage.SessionStarted),
                    ("recording_id", operation.RecordingId, BppLogCorrelationPolicy.Short),
                    ("battle_id", operation.BattleId, BppLogCorrelationPolicy.Short),
                    ("pending_count", _operations.Count),
                ]
        );
    }

    private void StartAudioTaps(ReplayVideoRecordingOperation operation, string tempVideoPath)
    {
        // Spatialized combat and board effects bypass the FMOD channel groups, so audio comes from the rendered output.
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
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatReplay,
                        "combat_replay.audio_capture.started"
                    ),
                    () =>
                        [
                            ("recording_id", operation.RecordingId, BppLogCorrelationPolicy.Short),
                            ("backend", tap.Backend),
                            ("sample_rate_hz", tap.SampleRateHz),
                            ("channels", tap.Channels),
                            ("sample_format", tap.SampleFormat),
                        ]
                );
                return;
            }

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
            _activeDegradationReason = ReplayVideoRecordingReasonCode.AudioCaptureFailed;
            _activeDegradationException = ex;
            DeleteWavBestEffort(wavPath);
        }
    }

    // The History row is optional at start, so a locked database need not block Unity.
    private Task<MetadataWriteOutcome> SaveStartMetadataOffMainThread(
        ReplayVideoCaptureRequest request,
        IBppServices services
    )
    {
        var store = _metadataStore;
        if (store == null)
            return Task.FromResult(
                new MetadataWriteOutcome(
                    ReplayVideoMetadataStatus.Unavailable,
                    _metadataInitializationException
                )
            );

        // The services' paths are read on the main thread before dispatching the insert.
        VideoRecordingStarted started;
        try
        {
            started = CreateStartedMetadata(request, services);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new MetadataWriteOutcome(ReplayVideoMetadataStatus.Failed, ex));
        }

        return ReplayVideoAudioMuxer.DispatchTracked(() =>
        {
            try
            {
                store.SaveStart(started);
                return MetadataWriteOutcome.Complete;
            }
            catch (Exception ex)
            {
                return new MetadataWriteOutcome(ReplayVideoMetadataStatus.Failed, ex);
            }
        });
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
                VideoDirectory(services),
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
            catch { }
            _captureCoroutine = null;
        }
    }

    private ReplayVideoRecordingOperation? AbortActiveSession(
        string reason,
        Task? metadataWriteBefore = null
    )
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
        // Abort callers are about to start another capture or shut down, so they need the tap joined before returning.
        var wavPaths = _activeAudioWavPaths;
        var audioStop = Task.FromResult(
            operation == null
                ? ReplayAudioStopOutcome.Empty
                : ReplayAudioTapStopper.Stop(
                    ReplayAudioTapStopper.Detach(_audioTaps),
                    operation.RecordingId
                )
        );

        // Freeze all state before dispatch. ClearActiveState runs before this method returns, and a
        // superseding recording is then free to overwrite every _active* field.
        var tempPath = _activeRecordingTempPath ?? session?.Request.OutputFilePath ?? string.Empty;
        var finalPath =
            _activeRecordingFinalPath
            ?? (session == null ? string.Empty : session.Request.FinalOutputFilePath);
        var store = _metadataStore;
        var videoDir = _services == null ? null : VideoDirectory(_services);
        var startMetadata =
            _activeStartMetadata ?? Task.FromResult(MetadataWriteOutcome.Unavailable);
        if (metadataWriteBefore != null)
            startMetadata = AfterWrite(metadataWriteBefore, startMetadata);
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
                catch { }
            }
        }

        DisposeUiState();
        ClearActiveState();
        if (operation == null)
        {
            DeleteTempFile(tempPath, recordingId: null);
            DeleteWavBestEffort(wavPaths);
            return null;
        }

        if (drain != null)
        {
            var recordingContext = new RecordingFinalizeContext(
                operations,
                operation,
                drain,
                tempPath,
                finalPath,
                wavPaths,
                audioStop
            );
            var abortedContext = new AbortedRecordingFinalizeContext(
                recordingContext,
                reason,
                store,
                videoDir,
                startMetadata,
                terminalReason,
                terminalException
            );
            ReplayVideoAudioMuxer.DispatchTracked(() => CompleteAbortedRecording(abortedContext));
            return operation;
        }

        ReplayVideoAudioMuxer.DispatchTracked(() =>
            CompleteAbortedWithoutDrain(
                operations,
                operation,
                tempPath,
                finalPath,
                wavPaths,
                audioStop,
                startMetadata,
                terminalReason,
                terminalException
            )
        );
        return operation;
    }

    private static IDisposable? BeginUiSuppression(string recordingId)
    {
        try
        {
            // Full-screen capture keeps the combat status bar in the MP4.
            return UiSuppressionScope.Begin(
                () => BppUiChromeSuppression.Begin(BppUiChromeSuppressionMode.ReplayRecording),
                () =>
                    NativeTooltipSuppression.Begin(
                        NativeTooltipSuppressionOwner.ReplayVideoRecording
                    )
            );
        }
        catch (Exception ex)
        {
            BppLog.DebugEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.video_recording.cleanup_failed"
                ),
                ex,
                () =>
                    [
                        ("recording_id", recordingId, BppLogCorrelationPolicy.Short),
                        ("stage", ReplayVideoLogStage.UiSuppression),
                        ("path", null),
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
            catch { }
            _uiSuppressionScope = null;
        }
    }

    private ReplayVideoCaptureRequest? BuildCaptureRequest(
        string recordingId,
        CombatReplayPlaybackStarting evt,
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

        var backend = ReplayVideoBackendPolicy.Current;
        ReplayVideoEncoderProfile encoderProfile;
        if (backend == ReplayVideoBackend.MacNative)
        {
            encoderProfile = ReplayVideoEncoderProfile.NativeVideoToolbox(width, height, fps);
        }
        else if (backend == ReplayVideoBackend.WindowsNative)
        {
            encoderProfile = ReplayVideoEncoderProfile.NativeMediaFoundation(width, height, fps);
        }
        else
            return null;

        var nowLocal = DateTimeOffset.Now;
        var datePart = nowLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var stampPart = nowLocal.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
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
            _currentReplayAvailability = new CurrentReplayRecorderAvailability(phase, reason);
            return _currentReplayAvailability;
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

    // Finalization joins the start metadata before its SaveFinish; this makes it also wait for
    // an earlier write to the same row.
    private static async Task<MetadataWriteOutcome> AfterWrite(
        Task earlierWrite,
        Task<MetadataWriteOutcome> startMetadata
    )
    {
        try
        {
            await earlierWrite.ConfigureAwait(false);
        }
        catch
        {
            // The earlier write is best effort; only its ordering matters here.
        }
        return await startMetadata.ConfigureAwait(false);
    }

    private Task TryMarkPreparedMetadataFailed(
        PreparedCurrentReplayRecording prepared,
        string error
    )
    {
        var store = _metadataStore;
        if (store == null)
            return Task.CompletedTask;

        var finished = new VideoRecordingFinished
        {
            VideoId = prepared.Operation.RecordingId,
            VideoRelativePath = ComputeRelativePath(
                _services == null ? null : VideoDirectory(_services),
                prepared.Request.FinalOutputFilePath
            ),
            EndedAtUtc = DateTimeOffset.UtcNow,
            DurationMs = 0,
            CapturedFrames = 0,
            DroppedFrames = 0,
            FileSizeBytes = null,
            Status = "FAILED",
            Error = error,
        };
        // The arm already reserved the row. OnDestroy's shutdown drain waits for the queued write after OnDisable cancels.
        return ReplayVideoAudioMuxer.DispatchTracked(() =>
        {
            try
            {
                store.SaveFinish(finished);
            }
            catch { }
        });
    }

    private static string VideoDirectory(IBppServices services) =>
        PathConstants.CombatReplayVideos(services.Paths.RequireDataRoot());

    private void OnOperationCompleted(ReplayVideoRecordingTerminal terminal)
    {
        if (_artifactProtectionLeases.TryRemove(terminal.RecordingId, out var protection))
            protection.Dispose();
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

    private void StartVideoMaintenance(
        CombatReplayVideoMetadataStore metadataStore,
        string videoDirectoryPath
    )
    {
        _videoMaintenanceFlight ??= new ReplayMaintenanceFlight();
        _videoMaintenanceFlight.Start(token =>
        {
            try
            {
                var result = new ReplayVideoArtifactMaintenanceService(
                    metadataStore,
                    videoDirectoryPath,
                    ReplayVideoInFlightArtifacts.SnapshotPaths
                ).Run(
                    DateTimeOffset.UtcNow,
                    ReplayVideoArtifactMaintenanceService.DefaultTemporaryRetention,
                    token
                );
                var fields = BuildVideoMaintenanceFields(
                    result.DeleteFailureCount == 0 ? "completed" : "degraded",
                    result
                );
                if (result.DeleteFailureCount == 0)
                    BppLog.DebugEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.CombatReplay,
                            "combat_replay.video_artifact.maintenance_observed"
                        ),
                        () => fields
                    );
                else
                    BppLog.WarnEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.CombatReplay,
                            "combat_replay.video_artifact.maintenance_observed"
                        ),
                        fields
                    );
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatReplay,
                        "combat_replay.video_artifact.maintenance_observed"
                    ),
                    ex,
                    BuildVideoMaintenanceFields("failed", default)
                );
            }
        });
    }

    private static BazaarPlusPlus.Infrastructure.Logging.BppLogField[] BuildVideoMaintenanceFields(
        string status,
        ReplayVideoMaintenanceResult result
    ) =>
        [
            ("status", status),
            ("metadata_count", result.MetadataCount),
            ("present_count", result.PresentMetadataCount),
            ("missing_count", result.MissingMetadataCount),
            ("unknown_mp4_count", result.UnknownSuccessfulMp4Count),
            ("temp_candidate_count", result.TempCandidateCount),
            ("temp_deleted_count", result.TempDeletedCount),
            ("failed_count", result.DeleteFailureCount),
            ("unmanaged_count", result.UnmanagedMetadataCount),
        ];

    private sealed record PreparedCurrentReplayRecording(
        ReplayVideoRecordingOperation Operation,
        ReplayVideoCaptureRequest Request
    );
}
