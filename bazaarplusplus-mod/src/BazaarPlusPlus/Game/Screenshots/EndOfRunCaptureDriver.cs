#nullable enable
using System.Collections;
using System.Reflection;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.OverlayPanels;
using BazaarPlusPlus.GameInterop.Tooltips;
using BazaarPlusPlus.Storage.Paths;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;
#if DEBUG
using System.Diagnostics;
using Unity.Profiling;
#endif

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunCaptureDriver
    : MonoBehaviour,
        IEndOfRunCaptureSurface<EndOfRunScreenController>
{
    private const float ControllerScanIntervalSeconds = 0.5f;
    private const string ActiveControllerFieldName = "_activeController";
    private static readonly FieldInfo? ActiveControllerField =
        typeof(EndOfRunScreenController).GetField(
            ActiveControllerFieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
#if DEBUG
    private static readonly ProfilerMarker ReadinessSampleMarker = new(
        "BPP.EndOfRunCapture.ReadinessSample"
    );
    private static readonly ProfilerMarker CleanFrameSampleMarker = new(
        "BPP.EndOfRunCapture.CleanFrameSample"
    );
#endif
    private readonly EndOfRunMouseBlocker _mouseBlocker = new();
    private readonly EndOfRunVisualStabilityTracker _visualStabilityTracker = new();
    private readonly EndOfRunSummaryVisualSnapshotSampler _visualSampler = new();
    private readonly EndOfRunHeavySampleCadence _readinessSampleCadence = new();
#if DEBUG
    private readonly EndOfRunCaptureSamplingDiagnostics _samplingDiagnostics = new();
#endif
    private EndOfRunCaptureWorkflow? _workflow;
    private ScreenshotService? _screenshotService;
    private IDisposable? _runInitializedSubscription;
    private EndOfRunScreenController? _cachedScreen;
    private int _visualStabilityScreenId;
    private int _visualStabilitySummaryId;
    private float _nextControllerScanAtSeconds;
    private IBppServices? _services;

    public bool IsAvailable => isActiveAndEnabled && _screenshotService != null;

    internal void Initialize(EndOfRunCaptureWorkflow workflow, IBppServices services)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _screenshotService = new ScreenshotService(
            PathConstants.Screenshots(services.Paths.RequireDataRoot()),
            services.RunSnapshot
        );

        _workflow.AttachDriver(this);
        SubscribeRunInitializedIfReady();
    }

    private void OnEnable()
    {
        Events.RunStarted.AddListener(OnRunStarted, this);
        Events.EndOfRunScreenInitializing.AddListener(OnEndOfRunScreenInitializing, this);
        if (_workflow != null)
            _workflow.AttachDriver(this);
        SubscribeRunInitializedIfReady();
    }

    private void OnDisable()
    {
        Events.RunStarted.RemoveListener(OnRunStarted);
        Events.EndOfRunScreenInitializing.RemoveListener(OnEndOfRunScreenInitializing);
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
        _workflow?.DetachDriver(this);
        _mouseBlocker.Destroy();
        ResetVisualStability();
#if DEBUG
        _samplingDiagnostics.Reset();
#endif
        _cachedScreen = null;
        _nextControllerScanAtSeconds = 0f;
    }

    private void OnDestroy()
    {
        _workflow?.DetachDriver(this);
        _mouseBlocker.Destroy();
    }

    private void Update() => _workflow?.OnFrame();

    public EndOfRunScreenController? FindActiveScreen()
    {
        var cached = _cachedScreen;
        if (cached != null)
        {
            if (cached.gameObject.activeInHierarchy)
                return cached;
            _cachedScreen = null;
        }
        if (AppState.CurrentState is not { } appState || !appState.IsEndOfRunState())
            return null;

        var now = Time.realtimeSinceStartup;
        if (now < _nextControllerScanAtSeconds)
            return null;
        _nextControllerScanAtSeconds = now + ControllerScanIntervalSeconds;

        foreach (
            var controller in UnityEngine.Object.FindObjectsOfType<EndOfRunScreenController>(
                includeInactive: true
            )
        )
        {
            if (controller != null && controller.gameObject.activeInHierarchy)
            {
                _cachedScreen = controller;
                return controller;
            }
        }
        return null;
    }

    public bool IsScreenActive(EndOfRunScreenController screen) =>
        screen != null && screen.gameObject.activeInHierarchy;

    public int GetScreenId(EndOfRunScreenController screen) => screen.GetInstanceID();

    public EndOfRunCaptureReadinessOutcome GetReadiness(
        EndOfRunScreenController screen,
        bool hasRevealStarted
    ) =>
        EndOfRunCaptureWorkflow.ReadReadiness(
            screen,
            hasRevealStarted,
            ObserveVisualSettlement(screen, hasRevealStarted)
        );

    public IEndOfRunCaptureAttempt BeginCapture(
        EndOfRunScreenController screen,
        ScreenshotCaptureRequest request
    )
    {
        if (_screenshotService == null)
            throw new InvalidOperationException("Screenshot capture service is unavailable.");
        return new UnityCaptureAttempt(
            this,
            screen,
            request,
            _screenshotService.BeginCaptureCurrentFrame
        );
    }

    public void SetContinueBlocked(EndOfRunScreenController? screen, bool blocked)
    {
        if (blocked && screen != null)
            _mouseBlocker.Attach(screen);
        else
            _mouseBlocker.Detach();
    }

    private void OnRunStarted()
    {
        ResetVisualStability();
#if DEBUG
        _samplingDiagnostics.Reset();
#endif
        _cachedScreen = null;
        _nextControllerScanAtSeconds = 0f;
        _workflow?.OnRunStarted();
    }

    private void OnEndOfRunScreenInitializing()
    {
        ResetVisualStability();
#if DEBUG
        _samplingDiagnostics.Reset();
#endif
        _cachedScreen = null;
        _nextControllerScanAtSeconds = 0f;
        _workflow?.OnEndOfRunInitializing();
    }

    private void SubscribeRunInitializedIfReady()
    {
        if (!isActiveAndEnabled || _services == null || _runInitializedSubscription != null)
            return;
        _runInitializedSubscription = _services.EventBus.Subscribe<RunInitializedObserved>(
            observed => _workflow?.ObserveRunInitialized(observed)
        );
    }

    private bool ObserveVisualSettlement(EndOfRunScreenController screen, bool hasRevealStarted)
    {
        if (!hasRevealStarted || !TryGetActiveSummary(screen, out var summary))
        {
            ResetVisualStability();
            return false;
        }

        var screenId = screen.GetInstanceID();
        var summaryId = summary.GetInstanceID();
        if (_visualStabilityScreenId != screenId || _visualStabilitySummaryId != summaryId)
        {
            _visualStabilityTracker.Reset();
            _visualSampler.Reset();
            _readinessSampleCadence.Reset();
            _visualStabilityScreenId = screenId;
            _visualStabilitySummaryId = summaryId;
        }

        var now = Time.realtimeSinceStartup;
        if (!_readinessSampleCadence.ShouldSample(now, summaryId))
            return _visualStabilityTracker.ObserveCached();

        EndOfRunSummaryVisualSnapshot snapshot;
        bool captured;
#if DEBUG
        var startedAt = Stopwatch.GetTimestamp();
        using (ReadinessSampleMarker.Auto())
            captured = _visualSampler.TryCapture(summary, out snapshot);
        _samplingDiagnostics.RecordReadiness(startedAt, snapshot);
#else
        captured = _visualSampler.TryCapture(summary, out snapshot);
#endif
        if (!captured)
        {
            _visualStabilityTracker.Reset();
            return false;
        }

        return _visualStabilityTracker.Observe(
            snapshot.LoadedCardCount,
            snapshot.CardSetFingerprint,
            snapshot.PoseFingerprint,
            now
        );
    }

    private static bool TryGetActiveSummary(
        EndOfRunScreenController? screen,
        out EndOfRunSummaryController summary
    )
    {
        summary = null!;
        if (screen == null || ActiveControllerField == null)
            return false;

        try
        {
            if (
                ActiveControllerField.GetValue(screen)
                is not EndOfRunSummaryController activeSummary
            )
                return false;
            if (activeSummary == null)
                return false;
            summary = activeSummary;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ResetVisualStability()
    {
        _visualStabilityTracker.Reset();
        _visualSampler.Reset();
        _readinessSampleCadence.Reset();
        _visualStabilityScreenId = 0;
        _visualStabilitySummaryId = 0;
    }

    private EndOfRunCleanFrameVisualObservation CaptureCleanFrameVisual(
        EndOfRunScreenController screen
    ) =>
        TryGetActiveSummary(screen, out var summary)
            ? _visualSampler.CaptureCleanFrameVisual(summary)
            : EndOfRunCleanFrameVisualObservation.Unavailable;

    internal void ReportSamplingDiagnostics()
    {
#if DEBUG
        _samplingDiagnostics.ReportAndReset();
#endif
    }

    private sealed class UnityCaptureAttempt : IEndOfRunCaptureAttempt
    {
        private readonly object _gate = new();
        private readonly EndOfRunCaptureDriver _driver;
        private readonly EndOfRunScreenController _screen;
        private readonly ScreenshotCaptureRequest _request;
        private readonly Func<ScreenshotCaptureRequest, ScreenshotCaptureSession> _beginCapture;
        private readonly TaskCompletionSource<bool> _frameAcquired = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<EndOfRunCaptureAttemptOutcome> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly EndOfRunCaptureSuppressionLifecycle _suppression = new();
        private ScreenshotCaptureSession? _captureSession;
        private ScreenshotCaptureReasonCode? _preparationDegradationReason;
        private bool _canceled;
        private bool _hasCaptureStarted;

        internal UnityCaptureAttempt(
            EndOfRunCaptureDriver driver,
            EndOfRunScreenController screen,
            ScreenshotCaptureRequest request,
            Func<ScreenshotCaptureRequest, ScreenshotCaptureSession> beginCapture
        )
        {
            _driver = driver;
            _screen = screen;
            _request = request;
            _beginCapture = beginCapture;
            _driver.StartCoroutine(Run());
        }

        public Task<EndOfRunCaptureAttemptOutcome> Completion => _completion.Task;

        public Task FrameAcquired => _frameAcquired.Task;

        public bool HasCaptureStarted
        {
            get
            {
                lock (_gate)
                    return _hasCaptureStarted;
            }
        }

        public void Cancel()
        {
            ScreenshotCaptureSession? captureSession;
            lock (_gate)
            {
                if (_canceled)
                    return;
                _canceled = true;
                captureSession = _captureSession;
            }

            _suppression.ReleaseAll();
            if (captureSession != null)
                CompleteFromTaskWhenReady(captureSession.Completion);
            else
                CompleteCanceled();
        }

        public void RestoreUi() => _suppression.ReleaseAll();

        private IEnumerator Run()
        {
            yield return null;
            if (IsCanceled())
            {
                CompleteCanceled();
                yield break;
            }

            var cleanFramePreparationAvailable = false;
            try
            {
                cleanFramePreparationAvailable = InstallSuppression();
            }
            catch
            {
                _suppression.ReleaseAll();
                _preparationDegradationReason =
                    ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable;
            }

            _driver.ResetVisualStability();
            if (cleanFramePreparationAvailable)
            {
                var preparation = new EndOfRunCleanFramePreparationCore(Time.realtimeSinceStartup);
                var cadence = new EndOfRunHeavySampleCadence();
                var endOfFrame = new WaitForEndOfFrame();
                while (true)
                {
                    yield return endOfFrame;
                    if (IsCanceled())
                    {
                        _suppression.ReleaseAll();
                        CompleteCanceled();
                        yield break;
                    }

                    var now = Time.realtimeSinceStartup;
                    var decision = cadence.ShouldSample(now, _screen.GetInstanceID())
                        ? ObserveFreshCleanFrame(preparation, now)
                        : preparation.ObserveCached(now);
                    if (decision.Kind == EndOfRunCleanFrameDecisionKind.ObserveFresh)
                        decision = ObserveFreshCleanFrame(preparation, now);
                    if (decision.Kind == EndOfRunCleanFrameDecisionKind.Capture)
                    {
                        _preparationDegradationReason ??= decision.ReasonCode;
                        break;
                    }
                }
            }

            if (IsCanceled())
            {
                _suppression.ReleaseAll();
                CompleteCanceled();
                yield break;
            }

            ScreenshotCaptureSession? session;
            try
            {
                lock (_gate)
                    _hasCaptureStarted = true;
                session = _beginCapture(_request);
            }
            catch (Exception ex)
            {
                _suppression.ReleaseAll();
                _frameAcquired.TrySetException(ex);
                _completion.TrySetResult(
                    EndOfRunCaptureAttemptOutcome.Failed(
                        ScreenshotCaptureReasonCode.CaptureSynchronousException,
                        ex
                    )
                );
                yield break;
            }

            if (session == null)
            {
                _suppression.ReleaseAll();
                _frameAcquired.TrySetException(
                    new InvalidOperationException("Screenshot capture returned no session.")
                );
                _completion.TrySetResult(
                    EndOfRunCaptureAttemptOutcome.Failed(
                        ScreenshotCaptureReasonCode.CaptureReturnedNull
                    )
                );
                yield break;
            }

            lock (_gate)
                _captureSession = session;
            ObserveFrameAcquired(session.FrameAcquired);
            if (IsCanceled())
            {
                _suppression.ReleaseAll();
                CompleteFromTaskWhenReady(session.Completion);
                yield break;
            }

            while (!session.Completion.IsCompleted && !IsCanceled())
                yield return null;

            if (IsCanceled())
            {
                _suppression.ReleaseAll();
                CompleteFromTaskWhenReady(session.Completion);
                yield break;
            }

            CompleteFromTask(session.Completion);
        }

        private EndOfRunCleanFrameDecision ObserveFreshCleanFrame(
            EndOfRunCleanFramePreparationCore preparation,
            float nowSeconds
        )
        {
#if DEBUG
            var startedAt = Stopwatch.GetTimestamp();
            var diagnosticTooltipAudit = default(NativeTooltipCleanFrameAudit);
            var diagnosticVisual = EndOfRunCleanFrameVisualObservation.Unavailable;
            EndOfRunCleanFrameDecision decision;
            using (CleanFrameSampleMarker.Auto())
            {
                decision = _suppression.ObserveCleanFrame(
                    preparation,
                    () => _driver.CaptureCleanFrameVisual(_screen),
                    nowSeconds,
                    (tooltipAudit, visual) =>
                    {
                        diagnosticTooltipAudit = tooltipAudit;
                        diagnosticVisual = visual;
                    }
                );
            }
            _driver._samplingDiagnostics.RecordBarrier(
                startedAt,
                diagnosticTooltipAudit,
                diagnosticVisual
            );
            return decision;
#else
            return _suppression.ObserveCleanFrame(
                preparation,
                () => _driver.CaptureCleanFrameVisual(_screen),
                nowSeconds
            );
#endif
        }

        private bool InstallSuppression()
        {
            var bppSuppression = BppUiChromeSuppression.Begin(
                BppUiChromeSuppressionMode.Screenshot
            );
            INativeTooltipSuppressionLease? nativeSuppression = null;
            try
            {
                nativeSuppression = NativeTooltipSuppression.Begin(
                    NativeTooltipSuppressionOwner.EndOfRunCapture
                );
            }
            catch
            {
                bppSuppression?.Dispose();
                throw;
            }

            return _suppression.TryInstall(bppSuppression, nativeSuppression);
        }

        private void ObserveFrameAcquired(Task frameAcquired)
        {
            _ = frameAcquired.ContinueWith(
                task =>
                {
                    if (task.IsCanceled)
                        _frameAcquired.TrySetCanceled();
                    else if (task.IsFaulted)
                        _frameAcquired.TrySetException(task.Exception!.GetBaseException());
                    else
                        _frameAcquired.TrySetResult(true);
                },
                TaskScheduler.Default
            );
        }

        private void CompleteFromTaskWhenReady(Task<ScreenshotCaptureResult?> task)
        {
            _ = task.ContinueWith(CompleteFromTask, TaskScheduler.Default);
        }

        private void CompleteFromTask(Task<ScreenshotCaptureResult?> task)
        {
            try
            {
                if (task.IsCanceled)
                {
                    CompleteCanceled();
                    return;
                }
                if (task.IsFaulted)
                {
                    _completion.TrySetResult(
                        EndOfRunCaptureAttemptOutcome.Failed(
                            ScreenshotCaptureReasonCode.CaptureTaskFaulted,
                            task.Exception?.GetBaseException()
                        )
                    );
                    return;
                }

                var capture = task.GetAwaiter().GetResult();
                _completion.TrySetResult(
                    capture == null
                        ? EndOfRunCaptureAttemptOutcome.Failed(
                            ScreenshotCaptureReasonCode.CaptureReturnedNull
                        )
                        : EndOfRunCaptureAttemptOutcome.Succeeded(
                            capture,
                            _preparationDegradationReason
                        )
                );
            }
            catch (Exception ex)
            {
                _completion.TrySetResult(
                    EndOfRunCaptureAttemptOutcome.Failed(
                        ScreenshotCaptureReasonCode.CaptureTaskFaulted,
                        ex
                    )
                );
            }
        }

        private void CompleteCanceled()
        {
            _frameAcquired.TrySetCanceled();
            _completion.TrySetResult(
                EndOfRunCaptureAttemptOutcome.Failed(ScreenshotCaptureReasonCode.ContextExpired)
            );
        }

        private bool IsCanceled()
        {
            lock (_gate)
                return _canceled;
        }
    }
}
