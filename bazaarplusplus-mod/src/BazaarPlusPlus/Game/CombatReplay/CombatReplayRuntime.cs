#nullable enable
using System.Collections;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay.Bootstrap;
using BazaarPlusPlus.Game.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Game.CombatReplay.Video;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.GameInterop.CombatReplay;
using BazaarPlusPlus.GameInterop.Files;
using BazaarPlusPlus.GameInterop.Tooltips;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class CombatReplayRuntime : MonoBehaviour
{
    internal const float CurrentReplayTerminalHoldSeconds = 2f;
    internal const float CurrentReplayRecapStableHoldSeconds = 1f;
    private const float CurrentReplayRecapTransitionTimeoutSeconds = 10f;

    private IBppServices? _services;
    private PvpBattleCaptureWindow<BazaarGameShared.Infra.Messages.INetMessage>? _captureWindow;
    private ReplayPersistenceOrchestrator? _persistence;
    private ReplayPlaybackPublisher? _playbackPublisher;
    private Func<CombatReplayVideoRecorder?>? _videoRecorder;
    private readonly CurrentReplayRecordingState _currentRecording = new();
    private PvpBattleManifest? _currentRecordingManifest;
    private IDisposable? _recordingStartedSubscription;
    private IDisposable? _recordingCompletedSubscription;
    private CombatReplayVideoRecordingStarted? _managedRecordingStarted;
    private CombatReplayVideoRecordingCompleted? _managedRecordingCompleted;
    private bool _managedRecordingFinalizing;
    private CurrentReplayRecordingSnapshot? _managedRecordingRestartFailure;
    private Coroutine? _pendingCurrentReplayStart;
    private Coroutine? _pendingCurrentReplayPresentationGate;
    private Coroutine? _pendingCurrentReplayRecapHold;
    private TaskCompletionSource<bool>? _currentReplaySimulationCompletion;
    private NetMessageCombatSim? _deferredCurrentReplaySimulation;
    private NetMessageCombatSim? _permittedCurrentReplaySimulation;
    private IDisposable? _currentReplayPresentationTooltipSuppression;
    private Action? _invokeRecordedReplayRecap;
    private bool _currentReplayRecapOwnsInputBlock;
    private bool _currentReplayRecapPreviousInputBlock;
    private bool _destroying;

    public static CombatReplayRuntime? Instance { get; private set; }

    /// <summary>Saved Replay Playback (ADR-0020); null until <see cref="Initialize"/>.</summary>
    public SavedReplayPlayback? SavedReplays { get; private set; }

    public bool IsSavedReplayPlaybackActive => SavedReplays?.IsSavedReplayPlaybackActive == true;

    public bool IsReplayStartInProgress => SavedReplays?.IsReplayStartInProgress == true;

    public bool HasPendingPersistence => _persistence?.HasPendingPersistence == true;

    private void Awake()
    {
        Instance = this;
    }

    public void Initialize(
        IBppServices services,
        RunLifecycleModule runLifecycle,
        IPvpBattleCatalog battleCatalog,
        Func<CombatReplayVideoRecorder?> videoRecorder
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        if (runLifecycle == null)
            throw new ArgumentNullException(nameof(runLifecycle));
        _videoRecorder = videoRecorder ?? throw new ArgumentNullException(nameof(videoRecorder));

        _persistence = new ReplayPersistenceOrchestrator(
            _services,
            battleCatalog,
            OnReplayPersistenceCompleted
        );
        _playbackPublisher = new ReplayPlaybackPublisher(_services);
        _captureWindow = new PvpBattleCaptureWindow<BazaarGameShared.Infra.Messages.INetMessage>(
            new DesktopPvpBattleReads(),
            _services.RunContext,
            _services.RunSnapshot,
            static () => DateTimeOffset.UtcNow,
            PvpOpeningKinds.PvpCombat | PvpOpeningKinds.MonsterCombat
        );
        var catalog = new SavedReplayCatalog(_persistence.Catalog, _persistence.PayloadStore);
        SavedReplays = new SavedReplayPlayback(
            _services,
            runLifecycle,
            _playbackPublisher,
            _persistence.OperationGate,
            catalog,
            new DesktopSavedReplayHost(
                catalog,
                new OpponentPortraitController(Destroy),
                ResetManagedRecordingUi
            )
        );

        Events.StateChanged.AddListener(OnStateChanged, this);
        Events.ReplayStarted.AddListener(OnNativeReplayStarted, this);
        Events.ReplayEnded.AddListener(OnNativeReplayEnded, this);
        _recordingStartedSubscription =
            _services.EventBus.Subscribe<CombatReplayVideoRecordingStarted>(
                OnVideoRecordingStarted
            );
        _recordingCompletedSubscription =
            _services.EventBus.Subscribe<CombatReplayVideoRecordingCompleted>(
                OnVideoRecordingCompleted
            );
    }

    private void Update()
    {
        _persistence?.DrainPendingResults();
        SavedReplays?.Tick();
    }

    private void OnDestroy()
    {
        ReplayOpeningStateRestorer.Cleanup();
        _destroying = true;
        var currentReplayWasActive = _currentRecording.NativeReplayStarted;
        CancelPendingCurrentReplayStart(
            "native-replay-runtime-destroyed-before-start",
            "Combat replay runtime was destroyed."
        );
        CancelCurrentReplayPresentationGate("Combat replay runtime was destroyed.");
        CancelCurrentReplayRecapHold();
        _invokeRecordedReplayRecap = null;
        DisposeCurrentReplayPresentationTooltipSuppression();
        if (currentReplayWasActive)
        {
            _playbackPublisher?.PublishEnded("runtime-destroyed", failed: true);
            _currentRecording.MarkReplayEnded("Combat replay runtime was destroyed.");
        }
        SavedReplays?.Dispose();
        _persistence?.Dispose();
        _recordingStartedSubscription?.Dispose();
        _recordingStartedSubscription = null;
        _recordingCompletedSubscription?.Dispose();
        _recordingCompletedSubscription = null;

        if (Instance == this)
            Instance = null;

        Events.StateChanged.RemoveListener(OnStateChanged);
        Events.ReplayStarted.RemoveListener(OnNativeReplayStarted);
        Events.ReplayEnded.RemoveListener(OnNativeReplayEnded);
    }

    public void ObserveMessage(BazaarGameShared.Infra.Messages.INetMessage message)
    {
        if (_captureWindow == null || _persistence == null)
            return;

        try
        {
            if (_captureWindow.Observe(message) is not { } capture)
                return;

            var manifest = capture.Manifest;
            var route = CapturedReplayRouter.Resolve(manifest);
            // A PvP battle whose replay parts failed is not latched or persisted, as before; the
            // window has logged which part failed.
            if (route == CapturedReplayRoute.PersistedPvp && capture.Replay is null)
            {
                BppLog.ErrorEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CombatReplay,
                        "combat_replay.capture.failed"
                    ),
                    ("run_id", manifest.RunId, BppLogCorrelationPolicy.Short),
                    ("reason_code", ReplayCaptureReasonCode.ReplayPartMissing)
                );
                return;
            }

            _currentRecordingManifest = manifest;
            _currentRecording.LatchBattle(manifest.BattleId);
            PrepareCurrentReplayRecordingAvailability();
            switch (route)
            {
                case CapturedReplayRoute.CurrentNative:
                    _currentRecording.MarkCurrentNativeReady(manifest.BattleId);
                    break;
                case CapturedReplayRoute.PersistedPvp:
                    _persistence.Enqueue(capture.Replay!, manifest);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.CombatReplay, "combat_replay.capture.failed"),
                ex,
                ("run_id", _services?.RunContext.CurrentServerRunId, BppLogCorrelationPolicy.Short),
                ("reason_code", ReplayCaptureReasonCode.CaptureOrEnqueueException)
            );
        }
    }

    internal CurrentReplayRecordingSnapshot GetCurrentReplayRecordingSnapshot()
    {
        RefreshCurrentReplayRecordingAvailability();
        var managedSnapshot = GetManagedReplayRecordingSnapshot();
        return ReplayRecordingButtonSnapshotPolicy.Resolve(
            managedSnapshot,
            _currentRecording.Snapshot()
        );
    }

    private CurrentReplayRecordingSnapshot GetManagedReplayRecordingSnapshot()
    {
        if (
            AppState.CurrentState is ReplayState
            && _managedRecordingRestartFailure is { } restartFailure
        )
        {
            return restartFailure;
        }

        var operation = SavedReplays?.Active;
        if (
            operation == null
            || AppState.CurrentState is not ReplayState
            || string.IsNullOrWhiteSpace(operation.BattleId)
        )
        {
            return default;
        }

        if (!operation.RecordVideo)
        {
            var availability = _videoRecorder?.Invoke()?.GetCurrentReplayRecordingAvailability();
            var sessionReady = string.Equals(
                _playbackPublisher?.ActiveSessionBattleId,
                operation.BattleId,
                StringComparison.Ordinal
            );
            var restartReadiness = NativeReplayRestartProbe.Observe(
                AppState.CurrentState as ReplayState
            );
            var replayReady = sessionReady && restartReadiness.CanRestart;
            var statusCode =
                !sessionReady ? CurrentReplayRecordingStatusCode.SessionChanged
                : !restartReadiness.CanRestart
                    ? CurrentReplayRecordingStatusCodes.FromNativeBlocker(restartReadiness.Blocker)
                : availability?.IsReady == true ? CurrentReplayRecordingStatusCode.None
                : CurrentReplayRecordingStatusCode.RecorderUnavailable;
            return ReplayRecordingButtonSnapshotPolicy.OrdinaryManagedReplay(
                operation.BattleId,
                availability?.IsReady == true,
                replayReady,
                statusCode,
                availability?.Reason
            );
        }

        var started =
            _managedRecordingStarted is { } candidate
            && string.Equals(candidate.BattleId, operation.BattleId, StringComparison.Ordinal)
                ? candidate
                : null;
        var completed =
            _managedRecordingCompleted is { } terminal
            && string.Equals(terminal.BattleId, operation.BattleId, StringComparison.Ordinal)
            && (
                started == null
                || string.Equals(
                    terminal.RecordingId,
                    started.RecordingId,
                    StringComparison.Ordinal
                )
            )
                ? terminal
                : null;

        var phase =
            completed == null
                ? _managedRecordingFinalizing
                    ? CurrentReplayRecordingPhase.Finalizing
                    : started == null
                        ? CurrentReplayRecordingPhase.Preparing
                        : CurrentReplayRecordingPhase.Recording
                : !completed.ArtifactUsable
                    ? CurrentReplayRecordingPhase.Failed
                    : completed.MetadataStatus == ReplayVideoMetadataStatus.Complete
                    && completed.ReasonCode == ReplayVideoRecordingReasonCode.Completed
                        ? CurrentReplayRecordingPhase.Succeeded
                        : CurrentReplayRecordingPhase.Degraded;
        var finalFilePath = completed?.ArtifactUsable == true ? completed.FinalFilePath : null;
        var canReveal =
            phase is CurrentReplayRecordingPhase.Succeeded or CurrentReplayRecordingPhase.Degraded
            && !string.IsNullOrWhiteSpace(finalFilePath);
        return new CurrentReplayRecordingSnapshot(
            phase,
            operation.BattleId,
            started?.RecordingId,
            finalFilePath,
            completed?.Reason,
            Visible: true,
            CanStart: false,
            CanReveal: canReveal,
            StatusCode: completed is { ArtifactUsable: false }
                ? CurrentReplayRecordingStatusCode.RecordingFailed
                : CurrentReplayRecordingStatusCode.None
        );
    }

    internal void PrepareCurrentReplayRecordingAvailability()
    {
        var availability = _videoRecorder?.Invoke()?.PrepareCurrentReplayRecordingAvailability();
        if (availability.HasValue)
        {
            _currentRecording.SetAvailability(
                availability.Value.IsReady,
                availability.Value.Reason
            );
        }
    }

    internal void BindNativeRecapAction(Action invokeNativeRecap)
    {
        _invokeRecordedReplayRecap =
            invokeNativeRecap ?? throw new ArgumentNullException(nameof(invokeNativeRecap));
    }

    internal bool TryStartCurrentReplayRecording(
        Action invokeNativeReplay,
        Action invokeNativeRecap,
        Action invokeNativeRecapBack,
        out CurrentReplayRecordingStatusCode statusCode
    )
    {
        if (invokeNativeReplay == null)
            throw new ArgumentNullException(nameof(invokeNativeReplay));
        if (invokeNativeRecap == null)
            throw new ArgumentNullException(nameof(invokeNativeRecap));
        if (invokeNativeRecapBack == null)
            throw new ArgumentNullException(nameof(invokeNativeRecapBack));

        if (SavedReplays?.Active?.RecordVideo == false)
            return TryStartManagedReplayRecording(
                invokeNativeReplay,
                invokeNativeRecap,
                out statusCode
            );

        RefreshCurrentReplayRecordingAvailability();
        var snapshot = _currentRecording.Snapshot();
        var manifest = _currentRecordingManifest;
        var recorder = _videoRecorder?.Invoke();
        if (!snapshot.CanStart || manifest == null || recorder == null)
        {
            statusCode =
                snapshot.StatusCode == CurrentReplayRecordingStatusCode.None
                    ? CurrentReplayRecordingStatusCode.RecorderUnavailable
                    : snapshot.StatusCode;
            return false;
        }

        var arm = recorder.TryArmCurrentReplay(manifest.BattleId);
        if (!arm.Succeeded || string.IsNullOrWhiteSpace(arm.RecordingId))
        {
            statusCode = CurrentReplayRecordingStatusCode.RecorderUnavailable;
            return false;
        }

        var recordingId = arm.RecordingId;
        if (!_currentRecording.TryArm(recordingId))
        {
            recorder.CancelArmedCurrentReplay(
                recordingId,
                "native-replay-state-changed-before-arm"
            );
            statusCode = CurrentReplayRecordingStatusCode.SessionChanged;
            return false;
        }

        _playbackPublisher!.BeginSession(
            manifest.BattleId,
            manifest,
            CombatReplayPlaybackSource.CurrentNative,
            recordVideo: true
        );
        _invokeRecordedReplayRecap = invokeNativeRecap;

        var boardManager = Singleton<BoardManager>.Instance;
        if (boardManager is { } && (boardManager.IsRecapViewOpen || boardManager.StorageMoving))
        {
            try
            {
                _pendingCurrentReplayStart = StartCoroutine(
                    StartCurrentReplayAfterRecapClosed(
                        recordingId,
                        invokeNativeReplay,
                        invokeNativeRecapBack
                    )
                );
                statusCode = CurrentReplayRecordingStatusCode.None;
                return true;
            }
            catch (Exception ex)
            {
                FailCurrentReplayStart(
                    recordingId,
                    "native-recap-transition-start-failed",
                    ex.Message
                );
                statusCode = CurrentReplayRecordingStatusCode.NativeInvokeFailed;
                return false;
            }
        }

        return TryInvokeCurrentReplay(recordingId, invokeNativeReplay, out statusCode);
    }

    private bool TryStartManagedReplayRecording(
        Action invokeNativeReplay,
        Action invokeNativeRecap,
        out CurrentReplayRecordingStatusCode statusCode
    )
    {
        var restart = new ManagedReplayRecordingRestartCore();
        var savedReplays = SavedReplays;
        var operation = savedReplays?.Active;
        var publisher = _playbackPublisher;
        var replay = AppState.CurrentState as ReplayState;
        var restartReadiness = NativeReplayRestartProbe.Observe(replay);
        var sessionAvailable =
            operation != null
            && !operation.RecordVideo
            && publisher != null
            && string.Equals(
                publisher.ActiveSessionBattleId,
                operation.BattleId,
                StringComparison.Ordinal
            );
        var availability = _videoRecorder?.Invoke()?.GetCurrentReplayRecordingAvailability();
        var decision = restart.Begin(
            sessionAvailable,
            availability?.IsReady == true,
            restartReadiness.Blocker
        );
        if (decision.Action == ManagedReplayRecordingRestartAction.Blocked)
        {
            statusCode = decision.StatusCode;
            return false;
        }

        if (savedReplays == null || operation == null || publisher == null || replay == null)
            throw new InvalidOperationException(
                "Managed replay restart preflight was inconsistent."
            );

        var operationPromoted = savedReplays.TryPromoteActiveToRecording();
        var publisherPromoted =
            operationPromoted && publisher.TryPromoteActiveSessionToRecording(operation.BattleId);
        decision = restart.OnPromoted(operationPromoted, publisherPromoted);
        if (decision.Action == ManagedReplayRecordingRestartAction.CompleteFailed)
        {
            return FailManagedReplayRecordingRestart(
                savedReplays,
                operation,
                decision,
                exception: null,
                out statusCode
            );
        }

        ResetManagedRecordingUi();
        _invokeRecordedReplayRecap = invokeNativeRecap;
        var publish = publisher.PublishStarting();
        decision = restart.OnStartingPublished(publish.Succeeded);
        if (decision.Action == ManagedReplayRecordingRestartAction.CompleteFailed)
        {
            return FailManagedReplayRecordingRestart(
                savedReplays,
                operation,
                decision,
                publish.Exception,
                out statusCode
            );
        }

        Exception? invocationException = null;
        try
        {
            invokeNativeReplay();
        }
        catch (Exception ex)
        {
            invocationException = ex;
        }

        decision = restart.OnNativeInvoked(invocationException == null, replay.IsReplaying);
        if (decision.Action == ManagedReplayRecordingRestartAction.CompleteFailed)
        {
            return FailManagedReplayRecordingRestart(
                savedReplays,
                operation,
                decision,
                invocationException,
                out statusCode
            );
        }

        statusCode = CurrentReplayRecordingStatusCode.None;
        return true;
    }

    private bool FailManagedReplayRecordingRestart(
        SavedReplayPlayback savedReplays,
        ActiveSavedReplay operation,
        ManagedReplayRecordingRestartDecision decision,
        Exception? exception,
        out CurrentReplayRecordingStatusCode statusCode
    )
    {
        _invokeRecordedReplayRecap = null;
        var failureException = savedReplays.FailActiveRecordingRestart(
            decision.EndReason!,
            decision.FailureReasonCode,
            exception
        );
        statusCode = decision.StatusCode;
        _managedRecordingRestartFailure = new CurrentReplayRecordingSnapshot(
            CurrentReplayRecordingPhase.Failed,
            operation.BattleId,
            RecordingId: null,
            FinalFilePath: null,
            Reason: failureException?.Message,
            Visible: true,
            CanStart: false,
            CanReveal: false,
            StatusCode: statusCode
        );
        return false;
    }

    private IEnumerator StartCurrentReplayAfterRecapClosed(
        string recordingId,
        Action invokeNativeReplay,
        Action invokeNativeRecapBack
    )
    {
        const float recapCloseTimeoutSeconds = 10f;
        var timeoutAt = Time.realtimeSinceStartup + recapCloseTimeoutSeconds;
        while (true)
        {
            if (AppState.CurrentState is not ReplayState)
            {
                _pendingCurrentReplayStart = null;
                FailCurrentReplayStart(
                    recordingId,
                    "native-replay-state-exited-before-start",
                    "Replay state exited while the recap view was closing."
                );
                yield break;
            }

            var boardManager = Singleton<BoardManager>.Instance;
            if (boardManager == null)
            {
                _pendingCurrentReplayStart = null;
                FailCurrentReplayStart(
                    recordingId,
                    "native-replay-board-unavailable",
                    "The combat board is unavailable."
                );
                yield break;
            }

            if (boardManager.IsRecapViewOpen && !boardManager.StorageMoving && !AppState.BlockInput)
            {
                try
                {
                    invokeNativeRecapBack();
                }
                catch (Exception ex)
                {
                    _pendingCurrentReplayStart = null;
                    FailCurrentReplayStart(
                        recordingId,
                        "native-recap-back-invoke-failed",
                        ex.Message
                    );
                    yield break;
                }
            }

            if (!boardManager.IsRecapViewOpen && !boardManager.StorageMoving)
                break;

            if (Time.realtimeSinceStartup >= timeoutAt)
            {
                _pendingCurrentReplayStart = null;
                FailCurrentReplayStart(
                    recordingId,
                    "native-recap-close-timeout",
                    "The recap view did not finish closing."
                );
                yield break;
            }

            yield return null;
        }

        _pendingCurrentReplayStart = null;
        TryInvokeCurrentReplay(recordingId, invokeNativeReplay, out _);
    }

    private bool TryInvokeCurrentReplay(
        string recordingId,
        Action invokeNativeReplay,
        out CurrentReplayRecordingStatusCode statusCode
    )
    {
        try
        {
            invokeNativeReplay();
        }
        catch (Exception ex)
        {
            FailCurrentReplayStart(recordingId, "native-replay-invoke-failed", ex.Message);
            statusCode = CurrentReplayRecordingStatusCode.NativeInvokeFailed;
            return false;
        }

        if (!_currentRecording.NativeReplayStarted)
        {
            FailCurrentReplayStart(
                recordingId,
                "native-replay-not-started",
                "The native replay did not start."
            );
            statusCode = CurrentReplayRecordingStatusCode.NativeStartRejected;
            return false;
        }

        statusCode = CurrentReplayRecordingStatusCode.None;
        return true;
    }

    private void FailCurrentReplayStart(string recordingId, string endReason, string reason)
    {
        _videoRecorder?.Invoke()?.CancelArmedCurrentReplay(recordingId, endReason);
        _playbackPublisher?.PublishEnded(endReason, failed: true);
        _currentRecording.RollbackArm(recordingId, reason);
        _invokeRecordedReplayRecap = null;
    }

    private void CancelPendingCurrentReplayStart(string endReason, string reason)
    {
        var pending = _pendingCurrentReplayStart;
        if (pending == null)
            return;

        _pendingCurrentReplayStart = null;
        StopCoroutine(pending);
        var recordingId = _currentRecording.RecordingId;
        if (!string.IsNullOrWhiteSpace(recordingId))
            FailCurrentReplayStart(recordingId, endReason, reason);
    }

    internal bool TryRevealCurrentReplayVideo(out string reason)
    {
        var snapshot = GetCurrentReplayRecordingSnapshot();
        if (!snapshot.CanReveal || string.IsNullOrWhiteSpace(snapshot.FinalFilePath))
        {
            reason = snapshot.Reason ?? "Recorded video is unavailable.";
            return false;
        }

        return SystemFileRevealer.TryReveal(snapshot.FinalFilePath, out reason);
    }

    private void RefreshCurrentReplayRecordingAvailability()
    {
        var availability = _videoRecorder?.Invoke()?.GetCurrentReplayRecordingAvailability();
        if (!availability.HasValue)
            return;

        _currentRecording.SetAvailability(availability.Value.IsReady, availability.Value.Reason);
    }

    private void OnReplayPersistenceCompleted(
        PvpBattleManifest manifest,
        bool succeeded,
        Exception? error
    )
    {
        if (_destroying)
            return;
        _currentRecording.MarkBattlePersistence(manifest.BattleId, succeeded, error?.Message);
        if (succeeded)
            PrepareCurrentReplayRecordingAvailability();
    }

    internal bool TryDeferCurrentReplaySimulation(
        CombatSimHandler handler,
        NetMessageCombatSim message,
        CancellationTokenSource cancellationToken,
        out Task deferredSimulation
    )
    {
        deferredSimulation = Task.CompletedTask;

        // The coroutine invokes Simulate again after the presentation boundary. Permit exactly
        // that message reference once so the Harmony prefix publishes CombatSimObserved and hands
        // the simulation to Post-Combat Impact once, then reaches the native method.
        if (ReferenceEquals(_permittedCurrentReplaySimulation, message))
        {
            _permittedCurrentReplaySimulation = null;
            return false;
        }

        if (
            AppState.CurrentState is not ReplayState
            || !_currentRecording.NativeReplayStarted
            || _currentRecording.Snapshot().Phase != CurrentReplayRecordingPhase.Armed
        )
        {
            return false;
        }

        if (_currentReplaySimulationCompletion != null)
        {
            if (!ReferenceEquals(_deferredCurrentReplaySimulation, message))
                return false;

            deferredSimulation = _currentReplaySimulationCompletion.Task;
            return true;
        }

        var completion = new TaskCompletionSource<bool>();
        _currentReplaySimulationCompletion = completion;
        _deferredCurrentReplaySimulation = message;
        try
        {
            _pendingCurrentReplayPresentationGate = StartCoroutine(
                RunCurrentReplayPresentationGate(handler, message, cancellationToken, completion)
            );
            deferredSimulation = completion.Task;
            return true;
        }
        catch
        {
            _pendingCurrentReplayPresentationGate = null;
            _currentReplaySimulationCompletion = null;
            _deferredCurrentReplaySimulation = null;
            BeginCurrentReplayRecordingAtPresentationBoundary();
            return false;
        }
    }

    private IEnumerator RunCurrentReplayPresentationGate(
        CombatSimHandler handler,
        NetMessageCombatSim message,
        CancellationTokenSource cancellationToken,
        TaskCompletionSource<bool> completion
    )
    {
        var waitForRenderedFrame = new WaitForEndOfFrame();
        var startedAt = Time.realtimeSinceStartup;
        var stableFrameCount = 0;
        var snapshot = ObserveCurrentReplayPresentationReadiness();
        var timedOut = false;

        while (stableFrameCount < CurrentReplayPresentationReadiness.RequiredStableFrames)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                FailCurrentReplayPresentationGateBeforeRecording(
                    "native-replay-simulation-canceled-before-recording"
                );
                // Native CombatSimHandler treats cancellation as a successful early return.
                // Preserve that contract so ReplayState can still reverse the board and release
                // its own input lock instead of faulting its async-void replay workflow.
                CompleteDeferredCurrentReplaySimulation(completion);
                yield break;
            }

            yield return waitForRenderedFrame;

            snapshot = ObserveCurrentReplayPresentationReadiness();
            stableFrameCount = CurrentReplayPresentationReadiness.AdvanceStableFrameCount(
                stableFrameCount,
                snapshot
            );
            if (stableFrameCount >= CurrentReplayPresentationReadiness.RequiredStableFrames)
                break;

            if (
                Time.realtimeSinceStartup - startedAt
                < CurrentReplayPresentationReadiness.TimeoutSeconds
            )
            {
                continue;
            }

            timedOut = true;
            break;
        }

        LogCurrentReplayPresentationGate(
            timedOut
                ? CurrentReplayPresentationGateOutcome.TimedOut
                : CurrentReplayPresentationGateOutcome.Ready,
            snapshot,
            Time.realtimeSinceStartup - startedAt
        );
        BeginCurrentReplayRecordingAtPresentationBoundary();

        // Cross the rendered-frame boundary, then resume on the next Update. Even if this
        // coroutine runs before the recorder at EndOfFrame, the capture coroutine still queues
        // the clean pre-action frame before Simulate is invoked.
        yield return waitForRenderedFrame;
        yield return null;

        Task simulation;
        try
        {
            _permittedCurrentReplaySimulation = message;
            simulation = handler.Simulate(message, cancellationToken);
        }
        catch (Exception ex)
        {
            CompleteCurrentReplayRecording(
                "native-replay-simulation-invoke-failed",
                failed: true,
                ex.Message
            );
            CompleteDeferredCurrentReplaySimulation(completion, exception: ex);
            yield break;
        }

        while (!simulation.IsCompleted)
            yield return null;

        if (simulation.IsCanceled)
        {
            CompleteCurrentReplayRecording(
                "native-replay-simulation-canceled",
                failed: true,
                "The native replay simulation was canceled."
            );
            CompleteDeferredCurrentReplaySimulation(completion);
            yield break;
        }

        if (simulation.IsFaulted)
        {
            var exception =
                simulation.Exception?.GetBaseException()
                ?? new InvalidOperationException("The native replay simulation failed.");
            CompleteCurrentReplayRecording(
                "native-replay-simulation-failed",
                failed: true,
                exception.Message
            );
            CompleteDeferredCurrentReplaySimulation(completion, exception: exception);
            yield break;
        }

        // Preserve the game's native terminal slow-motion, then hold the final board before
        // ReplayState flips back to its post-combat presentation.
        yield return new WaitForSecondsRealtime(CurrentReplayTerminalHoldSeconds);
        yield return waitForRenderedFrame;
        CompleteDeferredCurrentReplaySimulation(completion);
    }

    private static CurrentReplayPresentationReadinessSnapshot ObserveCurrentReplayPresentationReadiness()
    {
        var boardManager = Singleton<BoardManager>.Instance;
        var replay = AppState.CurrentState as ReplayState;
        if (boardManager == null || replay == null)
        {
            return new CurrentReplayPresentationReadinessSnapshot(
                ReplayActive: false,
                BoardUpdating: boardManager?.IsUpdatingBoard == true,
                StorageMoving: boardManager?.StorageMoving == true,
                BoardPresentationUpdating: boardManager?.isUpdatingPresentation == true,
                CarpetUnrolling: boardManager?.IsCarpetUnrolling == true,
                BoardRevealing: boardManager?.IsRevealing == true,
                HasCardsToReveal: boardManager?.HasCardsToReveal() == true,
                PlayerSkillBoardUpdating: Data.PlayerSkillPresentationManager?.IsUpdatingSkillBoard
                    == true,
                OpponentSkillBoardUpdating: Data.OpponentSkillPresenationManager?.IsUpdatingSkillBoard
                    == true,
                ExpectedItemCount: 0,
                VisibleItemCount: 0,
                FaceUpItemCount: 0,
                SettledItemCount: 0,
                ExpectedSkillCount: 0,
                RegisteredSkillCount: 0,
                ReadySkillCount: 0
            );
        }

        var cards = Data.GetCards<Card>(ECombatantId.Player)
            .Concat(Data.GetCards<Card>(ECombatantId.Opponent))
            .ToArray();
        var expectedItems = cards
            .Where(card => card is ItemCard && card.Section == EInventorySection.Hand)
            .ToArray();
        var expectedSkills = cards.Where(card => card.Type == ECardType.Skill).ToArray();
        var visibleItemCount = 0;
        var faceUpItemCount = 0;
        var settledItemCount = 0;
        var registeredSkillCount = 0;
        var readySkillCount = 0;

        foreach (var card in expectedItems)
        {
            if (Data.CardAndSkillLookup.GetCardController(card) is not ItemController controller)
                continue;

            if (CurrentReplayPresentationReadiness.IsVisible(controller))
                visibleItemCount++;
            if (CurrentReplayPresentationReadiness.IsFaceUp(controller))
                faceUpItemCount++;
            if (CurrentReplayPresentationReadiness.IsSettled(controller))
                settledItemCount++;
        }

        foreach (var skill in expectedSkills)
        {
            var renderer = Data.CardAndSkillLookup.GetSkillProxyRenderer(skill);
            if (renderer == null)
                continue;

            registeredSkillCount++;
            if (CurrentReplayPresentationReadiness.IsSkillReady(renderer))
                readySkillCount++;
        }

        return new CurrentReplayPresentationReadinessSnapshot(
            ReplayActive: replay.IsReplaying,
            BoardUpdating: boardManager.IsUpdatingBoard,
            StorageMoving: boardManager.StorageMoving,
            BoardPresentationUpdating: boardManager.isUpdatingPresentation,
            CarpetUnrolling: boardManager.IsCarpetUnrolling,
            BoardRevealing: boardManager.IsRevealing,
            HasCardsToReveal: boardManager.HasCardsToReveal(),
            PlayerSkillBoardUpdating: Data.PlayerSkillPresentationManager?.IsUpdatingSkillBoard
                == true,
            OpponentSkillBoardUpdating: Data.OpponentSkillPresenationManager?.IsUpdatingSkillBoard
                == true,
            ExpectedItemCount: expectedItems.Length,
            VisibleItemCount: visibleItemCount,
            FaceUpItemCount: faceUpItemCount,
            SettledItemCount: settledItemCount,
            ExpectedSkillCount: expectedSkills.Length,
            RegisteredSkillCount: registeredSkillCount,
            ReadySkillCount: readySkillCount
        );
    }

    private void BeginCurrentReplayRecordingAtPresentationBoundary()
    {
        var outcome = _playbackPublisher?.PublishStarting();
        DisposeCurrentReplayPresentationTooltipSuppression();
        if (outcome is not { Succeeded: false })
            return;

        _playbackPublisher?.PublishEnded("starting-publish-failed", failed: true);
        _currentRecording.MarkReplayEnded(outcome.Value.Exception?.Message);
    }

    private void FailCurrentReplayPresentationGateBeforeRecording(string endReason)
    {
        var recordingId = _currentRecording.RecordingId;
        if (!string.IsNullOrWhiteSpace(recordingId))
            _videoRecorder?.Invoke()?.CancelArmedCurrentReplay(recordingId, endReason);
        _playbackPublisher?.PublishEnded(endReason, failed: true);
        _currentRecording.MarkReplayEnded("Replay presentation was canceled before recording.");
        DisposeCurrentReplayPresentationTooltipSuppression();
    }

    private void CompleteDeferredCurrentReplaySimulation(
        TaskCompletionSource<bool> completion,
        Exception? exception = null
    )
    {
        _pendingCurrentReplayPresentationGate = null;
        _currentReplaySimulationCompletion = null;
        _deferredCurrentReplaySimulation = null;
        _permittedCurrentReplaySimulation = null;

        if (exception != null)
        {
            completion.TrySetException(exception);
            return;
        }

        completion.TrySetResult(true);
    }

    private void CancelCurrentReplayPresentationGate(string reason)
    {
        var hadPendingGate = _currentReplaySimulationCompletion != null;
        if (_pendingCurrentReplayPresentationGate != null)
        {
            StopCoroutine(_pendingCurrentReplayPresentationGate);
            _pendingCurrentReplayPresentationGate = null;
        }

        var completion = _currentReplaySimulationCompletion;
        _currentReplaySimulationCompletion = null;
        _deferredCurrentReplaySimulation = null;
        _permittedCurrentReplaySimulation = null;
        DisposeCurrentReplayPresentationTooltipSuppression();
        if (
            hadPendingGate
            && _currentRecording.Snapshot().Phase == CurrentReplayRecordingPhase.Armed
            && !string.IsNullOrWhiteSpace(_currentRecording.RecordingId)
        )
        {
            _videoRecorder
                ?.Invoke()
                ?.CancelArmedCurrentReplay(
                    _currentRecording.RecordingId!,
                    "native-replay-presentation-gate-canceled"
                );
        }
        completion?.TrySetException(new InvalidOperationException(reason));
    }

    private void DisposeCurrentReplayPresentationTooltipSuppression()
    {
        _currentReplayPresentationTooltipSuppression?.Dispose();
        _currentReplayPresentationTooltipSuppression = null;
    }

    private void LogCurrentReplayPresentationGate(
        CurrentReplayPresentationGateOutcome outcome,
        CurrentReplayPresentationReadinessSnapshot snapshot,
        float elapsedSeconds
    )
    {
        BppLogField[] Fields() =>
            [
                ("recording_id", _currentRecording.RecordingId, BppLogCorrelationPolicy.Short),
                ("outcome", outcome),
                ("expected_items", snapshot.ExpectedItemCount),
                ("visible_items", snapshot.VisibleItemCount),
                ("face_up_items", snapshot.FaceUpItemCount),
                ("settled_items", snapshot.SettledItemCount),
                ("expected_skills", snapshot.ExpectedSkillCount),
                ("registered_skills", snapshot.RegisteredSkillCount),
                ("ready_skills", snapshot.ReadySkillCount),
                ("elapsed_ms", Math.Max(0, (int)Math.Round(elapsedSeconds * 1000f))),
            ];

        if (outcome == CurrentReplayPresentationGateOutcome.TimedOut)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.CombatReplay,
                    "combat_replay.current_recording.presentation_gate_resolved"
                ),
                Fields()
            );
            return;
        }

        BppLog.InfoEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.current_recording.presentation_gate_resolved"
            ),
            Fields()
        );
    }

    private void OnNativeReplayStarted()
    {
        if (!_currentRecording.MarkNativeReplayStarted())
            return;

        DisposeCurrentReplayPresentationTooltipSuppression();
        _currentReplayPresentationTooltipSuppression = NativeTooltipSuppression.Begin(
            NativeTooltipSuppressionOwner.ReplayPresentation
        );
    }

    private void OnNativeReplayEnded()
    {
        var currentNativeRecording = _currentRecording.NativeReplayStarted;
        var managedRecordedReplay = SavedReplays?.Active?.RecordVideo == true;
        if (!currentNativeRecording && !managedRecordedReplay)
            return;

        if (currentNativeRecording)
            DisposeCurrentReplayPresentationTooltipSuppression();
        if (
            currentNativeRecording
            && _currentRecording.Snapshot().Phase == CurrentReplayRecordingPhase.Armed
        )
        {
            var recordingId = _currentRecording.RecordingId;
            if (!string.IsNullOrWhiteSpace(recordingId))
            {
                _videoRecorder
                    ?.Invoke()
                    ?.CancelArmedCurrentReplay(
                        recordingId,
                        "native-replay-ended-before-recording-started"
                    );
            }
            CompleteCurrentReplayRecording(
                "native-replay-ended-before-recording-started",
                failed: true,
                "The native replay ended before video capture started."
            );
            return;
        }

        CancelCurrentReplayRecapHold();
        var invokeNativeRecap = _invokeRecordedReplayRecap;
        _invokeRecordedReplayRecap = null;
        if (invokeNativeRecap == null)
        {
            CompleteRecordedReplay(
                currentNativeRecording,
                "native-recap-action-unavailable",
                failed: true,
                "The native recap action is unavailable."
            );
            return;
        }

        try
        {
            // ReplayState starts rebuilding both boards without awaiting SpawnCombatCards, then
            // fires ReplayEnded. Recap skips any card whose controller is not registered yet, so
            // gate the native click on the same presentation readiness used at recording start.
            _pendingCurrentReplayRecapHold = StartCoroutine(
                OpenRecordedReplayRecapAfterBoardSettles(
                    currentNativeRecording,
                    invokeNativeRecap,
                    ResolveRecordedReplayRecapItemCount(_playbackPublisher?.ActiveSessionManifest)
                )
            );
        }
        catch (Exception ex)
        {
            try
            {
                CompleteRecordedReplay(
                    currentNativeRecording,
                    "native-recap-board-gate-start-failed",
                    failed: true,
                    ex.Message
                );
            }
            finally
            {
                RestoreCurrentReplayRecapInput();
            }
        }
    }

    private IEnumerator OpenRecordedReplayRecapAfterBoardSettles(
        bool currentNativeRecording,
        Action invokeNativeRecap,
        int? recordedItemCount
    )
    {
        BlockCurrentReplayRecapInput();
        var waitForRenderedFrame = new WaitForEndOfFrame();
        var startedAt = Time.realtimeSinceStartup;
        var stableFrameCount = 0;

        while (stableFrameCount < CurrentReplayPresentationReadiness.RequiredStableFrames)
        {
            yield return waitForRenderedFrame;

            if (AppState.CurrentState is not ReplayState)
            {
                FailRecordedReplayBeforeRecap(
                    currentNativeRecording,
                    "native-recap-state-exited-before-open",
                    "Replay state exited before the recap could open."
                );
                yield break;
            }

            var snapshot = ObserveCurrentReplayPresentationReadiness();
            stableFrameCount = CurrentReplayPresentationReadiness.AdvanceRecapStableFrameCount(
                stableFrameCount,
                snapshot,
                recordedItemCount
            );
            if (stableFrameCount >= CurrentReplayPresentationReadiness.RequiredStableFrames)
                break;

            if (
                Time.realtimeSinceStartup - startedAt
                < CurrentReplayPresentationReadiness.TimeoutSeconds
            )
            {
                continue;
            }

            FailRecordedReplayBeforeRecap(
                currentNativeRecording,
                "native-recap-board-readiness-timeout",
                "The replay board did not finish rebuilding, so the recap could not open."
            );
            yield break;
        }

        // The native button rejects programmatic clicks while input is blocked. Release our
        // short post-replay lease immediately before invoking it; Recap() then owns its native
        // 0.5-second input block while the board flips.
        RestoreCurrentReplayRecapInput();
        try
        {
            invokeNativeRecap();
        }
        catch (Exception ex)
        {
            _pendingCurrentReplayRecapHold = null;
            CompleteRecordedReplay(
                currentNativeRecording,
                "native-recap-invoke-failed",
                failed: true,
                ex.Message
            );
            yield break;
        }

        if (Singleton<BoardManager>.Instance?.IsRecapViewOpen != true)
        {
            _pendingCurrentReplayRecapHold = null;
            CompleteRecordedReplay(
                currentNativeRecording,
                "native-recap-not-started",
                failed: true,
                "The native recap did not start."
            );
            yield break;
        }

        yield return CompleteRecordedReplayAfterRecapSettles(currentNativeRecording);
    }

    private static int? ResolveRecordedReplayRecapItemCount(PvpBattleManifest? manifest)
    {
        var playerItemCount = CapturedItemCount(manifest?.Snapshots?.PlayerHand);
        var opponentItemCount = CapturedItemCount(manifest?.Snapshots?.OpponentHand);
        return playerItemCount.HasValue && opponentItemCount.HasValue
            ? playerItemCount.Value + opponentItemCount.Value
            : null;
    }

    private static int? CapturedItemCount(PvpBattleCardSetCapture? capture) =>
        capture?.Status is PvpBattleCaptureStatus.Captured or PvpBattleCaptureStatus.CapturedEmpty
            ? capture.Items.Count(card => card?.Type == CardType.Item)
            : null;

    private void BlockCurrentReplayRecapInput()
    {
        if (_currentReplayRecapOwnsInputBlock)
            return;

        _currentReplayRecapPreviousInputBlock = AppState.BlockInput;
        AppState.BlockInput = true;
        _currentReplayRecapOwnsInputBlock = true;
    }

    private void FailRecordedReplayBeforeRecap(
        bool currentNativeRecording,
        string endReason,
        string reason
    )
    {
        _pendingCurrentReplayRecapHold = null;
        try
        {
            CompleteRecordedReplay(currentNativeRecording, endReason, failed: true, reason);
        }
        finally
        {
            RestoreCurrentReplayRecapInput();
        }
    }

    private IEnumerator CompleteRecordedReplayAfterRecapSettles(bool currentNativeRecording)
    {
        var timeoutAt = Time.realtimeSinceStartup + CurrentReplayRecapTransitionTimeoutSeconds;
        var recapTransitionObserved = false;
        while (true)
        {
            if (AppState.CurrentState is not ReplayState)
            {
                _pendingCurrentReplayRecapHold = null;
                CompleteRecordedReplay(
                    currentNativeRecording,
                    "native-recap-state-exited",
                    failed: true,
                    "Replay state exited while the recap was opening."
                );
                yield break;
            }

            var boardManager = Singleton<BoardManager>.Instance;
            if (boardManager == null || !boardManager.IsRecapViewOpen)
            {
                _pendingCurrentReplayRecapHold = null;
                CompleteRecordedReplay(
                    currentNativeRecording,
                    "native-recap-closed-before-capture",
                    failed: true,
                    "The native recap closed before video capture completed."
                );
                yield break;
            }

            recapTransitionObserved |= boardManager.StorageMoving;
            // ShowRecapView constructs recap cards asynchronously before setting StorageMoving.
            // Require the transition to start and then finish; observing only a false value could
            // mistake that asset-loading gap for a fully rendered recap. Recap() also owns the
            // game's input block for its first 0.5 seconds, so wait for that lease to settle too.
            if (recapTransitionObserved && !boardManager.StorageMoving && !AppState.BlockInput)
                break;

            if (Time.realtimeSinceStartup >= timeoutAt)
            {
                _pendingCurrentReplayRecapHold = null;
                CompleteRecordedReplay(
                    currentNativeRecording,
                    "native-recap-transition-timeout",
                    failed: true,
                    "The native recap did not finish opening."
                );
                yield break;
            }

            yield return null;
        }

        _currentReplayRecapPreviousInputBlock = AppState.BlockInput;
        AppState.BlockInput = true;
        _currentReplayRecapOwnsInputBlock = true;
        yield return new WaitForSecondsRealtime(CurrentReplayRecapStableHoldSeconds);
        yield return new WaitForEndOfFrame();
        // Resume once more after the final end-of-frame capture before publishing "ended";
        // otherwise coroutine ordering can stop the recorder just before that frame is queued.
        yield return null;
        _pendingCurrentReplayRecapHold = null;
        try
        {
            CompleteRecordedReplay(
                currentNativeRecording,
                "native-replay-recap-stable-hold-ended",
                failed: false,
                reason: null
            );
        }
        finally
        {
            RestoreCurrentReplayRecapInput();
        }
    }

    private void CompleteRecordedReplay(
        bool currentNativeRecording,
        string endReason,
        bool failed,
        string? reason
    )
    {
        if (currentNativeRecording)
        {
            CompleteCurrentReplayRecording(endReason, failed, reason);
            return;
        }

        _invokeRecordedReplayRecap = null;
        _managedRecordingFinalizing = true;
        _playbackPublisher?.PublishEnded(endReason, failed);
    }

    private void CompleteCurrentReplayRecording(string endReason, bool failed, string? reason)
    {
        DisposeCurrentReplayPresentationTooltipSuppression();
        _invokeRecordedReplayRecap = null;
        var outcome = _playbackPublisher?.PublishEnded(endReason, failed);
        _currentRecording.MarkReplayEnded(
            outcome is { Succeeded: false } ? outcome.Value.Exception?.Message : reason
        );
    }

    private void CancelCurrentReplayRecapHold()
    {
        var pending = _pendingCurrentReplayRecapHold;
        if (pending != null)
        {
            _pendingCurrentReplayRecapHold = null;
            StopCoroutine(pending);
        }

        RestoreCurrentReplayRecapInput();
    }

    private void RestoreCurrentReplayRecapInput()
    {
        if (!_currentReplayRecapOwnsInputBlock)
            return;

        AppState.BlockInput = _currentReplayRecapPreviousInputBlock;
        _currentReplayRecapOwnsInputBlock = false;
        _currentReplayRecapPreviousInputBlock = false;
    }

    private void OnVideoRecordingStarted(CombatReplayVideoRecordingStarted started)
    {
        _currentRecording.MarkRecordingStarted(started.RecordingId, started.BattleId);
        if (
            started.Source != CombatReplayPlaybackSource.CurrentNative
            && SavedReplays?.Active is { RecordVideo: true } active
            && string.Equals(active.BattleId, started.BattleId, StringComparison.Ordinal)
        )
        {
            _managedRecordingStarted = started;
        }
    }

    private void OnVideoRecordingCompleted(CombatReplayVideoRecordingCompleted completed)
    {
        _currentRecording.ApplyCompletion(completed);
        if (
            completed.Source != CombatReplayPlaybackSource.CurrentNative
            && SavedReplays?.Active is { RecordVideo: true } active
            && string.Equals(active.BattleId, completed.BattleId, StringComparison.Ordinal)
        )
        {
            _managedRecordingCompleted = completed;
            _managedRecordingFinalizing = false;
        }
    }

    private void OnStateChanged(StateChangedEvent data)
    {
        if (data == null)
            return;

        if (data.PreviousState is not ReplayState && data.CurrentState is ReplayState)
        {
            _currentRecording.EnterReplayState();
            PrepareCurrentReplayRecordingAvailability();
            return;
        }

        if (data.PreviousState is not ReplayState || data.CurrentState is ReplayState)
            return;

        CancelPendingCurrentReplayStart(
            "native-replay-state-exited-before-start",
            "Replay state exited before the native replay could start."
        );
        var currentReplayWasActive = _currentRecording.NativeReplayStarted;
        CancelCurrentReplayPresentationGate(
            "Replay state exited before the recorded simulation completed."
        );
        CancelCurrentReplayRecapHold();
        _invokeRecordedReplayRecap = null;
        if (currentReplayWasActive)
        {
            var currentEnded = _playbackPublisher?.PublishEnded("replay-state-exit", failed: true);
            _currentRecording.MarkReplayEnded(
                currentEnded is { Succeeded: false }
                    ? currentEnded.Value.Exception?.Message
                    : "Replay state exited before the native replay ended."
            );
        }
        _currentRecording.LeaveReplayState();
        _currentRecordingManifest = null;
        ResetManagedRecordingUi();
        SavedReplays?.OnReplayStateExited();
    }

    // The bootstrapped ReplayState.Exit prefix (CombatReplayStateExitPatch).
    internal static bool TryExitBootstrappedSavedReplayToMenu() =>
        Instance?.SavedReplays?.TryExitBootstrapped() ?? false;

    private void ResetManagedRecordingUi()
    {
        _managedRecordingStarted = null;
        _managedRecordingCompleted = null;
        _managedRecordingFinalizing = false;
        _managedRecordingRestartFailure = null;
    }

    // Patches/Combat/CombatReplayVisualPatches.cs calls this static facade — keep the surface.
    public static void HideEncounterPickerOverlays() =>
        HealthBarBinder.HideEncounterPickerOverlays();
}
