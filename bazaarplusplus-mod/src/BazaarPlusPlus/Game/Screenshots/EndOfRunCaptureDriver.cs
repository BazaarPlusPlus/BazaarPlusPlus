#nullable enable
using System.Collections;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.OverlayPanels;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunCaptureDriver
    : MonoBehaviour,
        IEndOfRunCaptureSurface<EndOfRunScreenController>
{
    private const float ControllerScanIntervalSeconds = 0.5f;
    private readonly EndOfRunMouseBlocker _mouseBlocker = new();
    private EndOfRunCaptureWorkflow? _workflow;
    private ScreenshotService? _screenshotService;
    private IDisposable? _runInitializedSubscription;
    private EndOfRunScreenController? _cachedScreen;
    private float _nextControllerScanAtSeconds;
    private IBppServices? _services;

    public bool IsAvailable => isActiveAndEnabled && _screenshotService != null;

    internal void Initialize(EndOfRunCaptureWorkflow workflow, IBppServices services)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(services.Paths.ScreenshotsDirectoryPath))
            ScreenshotCaptureDiagnostics.ReportInitializationFailed();
        else
            _screenshotService = new ScreenshotService(services.Paths.ScreenshotsDirectoryPath);

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
    ) => EndOfRunCaptureWorkflow.ReadReadiness(screen, hasRevealStarted);

    public IEndOfRunCaptureAttempt BeginCapture(
        EndOfRunScreenController screen,
        ScreenshotCaptureRequest request
    )
    {
        if (_screenshotService == null)
            throw new InvalidOperationException("Screenshot capture service is unavailable.");
        return new UnityCaptureAttempt(this, request, _screenshotService.CaptureCurrentFrameAsync);
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
        _cachedScreen = null;
        _nextControllerScanAtSeconds = 0f;
        _workflow?.OnRunStarted();
    }

    private void OnEndOfRunScreenInitializing()
    {
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

    private sealed class UnityCaptureAttempt : IEndOfRunCaptureAttempt
    {
        private readonly object _gate = new();
        private readonly EndOfRunCaptureDriver _driver;
        private readonly ScreenshotCaptureRequest _request;
        private readonly Func<
            ScreenshotCaptureRequest,
            Task<ScreenshotCaptureResult?>
        > _captureAsync;
        private readonly TaskCompletionSource<EndOfRunCaptureAttemptOutcome> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private IDisposable? _suppression;
        private Task<ScreenshotCaptureResult?>? _captureTask;
        private bool _canceled;
        private bool _hasCaptureStarted;

        internal UnityCaptureAttempt(
            EndOfRunCaptureDriver driver,
            ScreenshotCaptureRequest request,
            Func<ScreenshotCaptureRequest, Task<ScreenshotCaptureResult?>> captureAsync
        )
        {
            _driver = driver;
            _request = request;
            _captureAsync = captureAsync;
            _driver.StartCoroutine(Run());
        }

        public Task<EndOfRunCaptureAttemptOutcome> Completion => _completion.Task;

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
            Task<ScreenshotCaptureResult?>? captureTask;
            lock (_gate)
            {
                if (_canceled)
                    return;
                _canceled = true;
                captureTask = _captureTask;
            }

            DisposeSuppression();
            if (captureTask != null)
                CompleteFromTaskWhenReady(captureTask);
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

            _suppression = BppUiChromeSuppression.Begin(BppUiChromeSuppressionMode.Screenshot);
            yield return new WaitForEndOfFrame();
            if (IsCanceled())
            {
                DisposeSuppression();
                CompleteCanceled();
                yield break;
            }

            Task<ScreenshotCaptureResult?>? task;
            try
            {
                lock (_gate)
                    _hasCaptureStarted = true;
                task = _captureAsync(_request);
            }
            catch (Exception ex)
            {
                DisposeSuppression();
                _completion.TrySetResult(
                    EndOfRunCaptureAttemptOutcome.Failed(
                        ScreenshotCaptureReasonCode.CaptureSynchronousException,
                        ex
                    )
                );
                yield break;
            }

            if (task == null)
            {
                DisposeSuppression();
                _completion.TrySetResult(
                    EndOfRunCaptureAttemptOutcome.Failed(
                        ScreenshotCaptureReasonCode.CaptureReturnedNull
                    )
                );
                yield break;
            }

            lock (_gate)
                _captureTask = task;
            if (IsCanceled())
            {
                DisposeSuppression();
                CompleteFromTaskWhenReady(task);
                yield break;
            }

            while (!task.IsCompleted && !IsCanceled())
                yield return null;

            if (IsCanceled())
            {
                DisposeSuppression();
                CompleteFromTaskWhenReady(task);
                yield break;
            }

            CompleteFromTask(task);
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

        private void CompleteCanceled() =>
            _completion.TrySetResult(
                EndOfRunCaptureAttemptOutcome.Failed(ScreenshotCaptureReasonCode.ContextExpired)
            );

        private bool IsCanceled()
        {
            lock (_gate)
                return _canceled;
        }

        private void DisposeSuppression()
        {
            IDisposable? suppression;
            lock (_gate)
            {
                suppression = _suppression;
                _suppression = null;
            }
            suppression?.Dispose();
        }
    }
}
