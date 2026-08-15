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
    private readonly EndOfRunMouseBlocker _mouseBlocker = new();
    private readonly EndOfRunVisualStabilityTracker _visualStabilityTracker = new();
    private EndOfRunCaptureWorkflow? _workflow;
    private ScreenshotService? _screenshotService;
    private IDisposable? _runInitializedSubscription;
    private EndOfRunScreenController? _cachedScreen;
    private int _visualStabilityScreenId;
    private float _nextControllerScanAtSeconds;
    private IBppServices? _services;

    public bool IsAvailable => isActiveAndEnabled && _screenshotService != null;

    internal void Initialize(EndOfRunCaptureWorkflow workflow, IBppServices services)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _screenshotService = new ScreenshotService(
            PathConstants.Screenshots(services.Paths.RequireDataRoot())
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
        _cachedScreen = null;
        _nextControllerScanAtSeconds = 0f;
        _workflow?.OnRunStarted();
    }

    private void OnEndOfRunScreenInitializing()
    {
        ResetVisualStability();
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
        if (_visualStabilityScreenId != screenId)
        {
            _visualStabilityTracker.Reset();
            _visualStabilityScreenId = screenId;
        }

        if (!EndOfRunSummaryVisualSnapshotSampler.TryCapture(summary, out var snapshot))
        {
            _visualStabilityTracker.Reset();
            return false;
        }

        return _visualStabilityTracker.Observe(
            snapshot.LoadedCardCount,
            snapshot.CardSetFingerprint,
            snapshot.PoseFingerprint,
            Time.realtimeSinceStartup
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
        _visualStabilityScreenId = 0;
    }

    private static EndOfRunCleanFrameVisualObservation CaptureCleanFrameVisual(
        EndOfRunScreenController screen
    ) =>
        TryGetActiveSummary(screen, out var summary)
            ? EndOfRunSummaryVisualSnapshotSampler.CaptureCleanFrameVisual(summary)
            : EndOfRunCleanFrameVisualObservation.Unavailable;

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
        private IDisposable? _bppSuppression;
        private INativeTooltipSuppressionLease? _nativeTooltipSuppression;
        private ScreenshotCaptureSession? _captureSession;
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

            DisposeSuppression();
            if (captureSession != null)
                CompleteFromTaskWhenReady(captureSession.Completion);
            else
                CompleteCanceled();
        }

        public void RestoreUi() => DisposeSuppression();

        private IEnumerator Run()
        {
            yield return null;
            if (IsCanceled())
            {
                CompleteCanceled();
                yield break;
            }

            Exception? suppressionException = null;
            try
            {
                InstallSuppression();
            }
            catch (Exception ex)
            {
                suppressionException = ex;
            }
            if (suppressionException != null)
            {
                DisposeSuppression();
                CompletePreparationFailure(
                    ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable,
                    suppressionException
                );
                yield break;
            }

            _driver.ResetVisualStability();
            var preparation = new EndOfRunCleanFramePreparationCore(Time.realtimeSinceStartup);
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (IsCanceled())
                {
                    DisposeSuppression();
                    CompleteCanceled();
                    yield break;
                }

                NativeTooltipCleanFrameAudit tooltipAudit;
                EndOfRunCleanFrameVisualObservation visual;
                try
                {
                    tooltipAudit =
                        _nativeTooltipSuppression?.AuditCleanFrame()
                        ?? new NativeTooltipCleanFrameAudit(
                            NativeTooltipCleanFrameState.Unavailable
                        );
                    visual = CaptureCleanFrameVisual(_screen);
                }
                catch (Exception ex)
                {
                    DisposeSuppression();
                    CompletePreparationFailure(
                        ScreenshotCaptureReasonCode.NativeTooltipSuppressionUnavailable,
                        ex
                    );
                    yield break;
                }

                var decision = preparation.Observe(tooltipAudit, visual, Time.realtimeSinceStartup);
                if (decision.Kind == EndOfRunCleanFrameDecisionKind.Capture)
                    break;
                if (decision.Kind == EndOfRunCleanFrameDecisionKind.Fail)
                {
                    DisposeSuppression();
                    CompletePreparationFailure(
                        decision.ReasonCode ?? ScreenshotCaptureReasonCode.CleanFrameDeadline,
                        exception: null
                    );
                    yield break;
                }
                yield return null;
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
                DisposeSuppression();
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
                DisposeSuppression();
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
                DisposeSuppression();
                CompleteFromTaskWhenReady(session.Completion);
                yield break;
            }

            while (!session.Completion.IsCompleted && !IsCanceled())
                yield return null;

            if (IsCanceled())
            {
                DisposeSuppression();
                CompleteFromTaskWhenReady(session.Completion);
                yield break;
            }

            CompleteFromTask(session.Completion);
        }

        private void InstallSuppression()
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

            lock (_gate)
            {
                if (_canceled)
                {
                    nativeSuppression.Dispose();
                    bppSuppression?.Dispose();
                    return;
                }
                _bppSuppression = bppSuppression;
                _nativeTooltipSuppression = nativeSuppression;
            }
        }

        private void CompletePreparationFailure(
            ScreenshotCaptureReasonCode reason,
            Exception? exception
        )
        {
            var failure = exception ?? new InvalidOperationException(reason.ToString());
            _frameAcquired.TrySetException(failure);
            _completion.TrySetResult(EndOfRunCaptureAttemptOutcome.Failed(reason, exception));
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
                        : EndOfRunCaptureAttemptOutcome.Succeeded(capture)
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

        private void DisposeSuppression()
        {
            IDisposable? bppSuppression;
            INativeTooltipSuppressionLease? nativeSuppression;
            lock (_gate)
            {
                bppSuppression = _bppSuppression;
                nativeSuppression = _nativeTooltipSuppression;
                _bppSuppression = null;
                _nativeTooltipSuppression = null;
            }
            try
            {
                nativeSuppression?.Dispose();
            }
            catch
            {
                // Native teardown is best-effort; BPP chrome must still be restored.
            }
            try
            {
                bppSuppression?.Dispose();
            }
            catch
            {
                // Logical fail-open is owned by the workflow even if native UI was destroyed.
            }
        }
    }
}
