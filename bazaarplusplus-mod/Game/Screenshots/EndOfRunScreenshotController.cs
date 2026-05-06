#nullable enable
using System;
using System.Collections;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.Screenshots.Persistence;
using BazaarPlusPlus.Game.Settings;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunScreenshotController : MonoBehaviour
{
    private const float CaptureRetryCooldownSeconds = 1f;
    private const float FirstCaptureDelaySeconds = 8f;
    private static readonly System.Reflection.MethodInfo ContinueClickMethod = AccessTools.Method(
        typeof(EndOfRunScreenController),
        "OnContinueClick"
    )!;
    private static EndOfRunScreenshotController? _current;
    private readonly EndOfRunScreenshotGate _gate = new();
    private readonly EndOfRunMouseBlocker _mouseBlocker = new();
    private ScreenshotService? _screenshotService;
    private RunScreenshotSqliteStore? _screenshotStore;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _captureSuppressionScope;
    private Coroutine? _captureCoroutine;
    private string? _bufferedRunId;
    private string? _bufferedHeroName;
    private bool? _lastLoggedBlockerActive;
    private string? _lastLoggedBlockerStateSummary;
    private int _trackedEndOfRunControllerId;
    private float _endOfRunEnteredAtSeconds = -1f;
    private IBppServices? _services;

    private void Awake()
    {
        _current = this;
    }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeCore();
    }

    private void InitializeCore()
    {
        var services = _services!;

        RefreshBufferedRunContext();

        var screenshotsDirectoryPath = services.Paths.ScreenshotsDirectoryPath;
        var runLogDatabasePath = services.Paths.RunLogDatabasePath;
        if (string.IsNullOrWhiteSpace(screenshotsDirectoryPath))
        {
            BppLog.Warn(
                "EndOfRunScreenshot",
                "Screenshot controller initialized without a screenshots directory."
            );
        }
        else
        {
            _screenshotService = new ScreenshotService(screenshotsDirectoryPath);
            if (!string.IsNullOrWhiteSpace(runLogDatabasePath))
                _screenshotStore = new RunScreenshotSqliteStore(runLogDatabasePath);
        }

        // Catch-up subscribe if OnEnable fired before Initialize (normal case: AddComponent → Awake → OnEnable → Initialize).
        if (isActiveAndEnabled && _runInitializedSubscription == null)
        {
            _runInitializedSubscription = services.EventBus.Subscribe<RunInitializedObserved>(
                OnRunInitializedObserved
            );
        }
    }

    private void OnEnable()
    {
        Events.RunStarted.AddListener(OnRunStarted, this);
        if (_services != null && _runInitializedSubscription == null)
        {
            _runInitializedSubscription = _services.EventBus.Subscribe<RunInitializedObserved>(
                OnRunInitializedObserved
            );
        }
    }

    private void OnDisable()
    {
        Events.RunStarted.RemoveListener(OnRunStarted);
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
        ResetCaptureUiState();
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;

        ResetCaptureUiState();
    }

    private void Update()
    {
        SyncEndOfRunMouseBlocker();
    }

    private void OnRunStarted()
    {
        _gate.ResetForNewRun();
        ResetCaptureUiState();
        ResetBufferedRunContext();
        RefreshBufferedRunContext();
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        if (!string.IsNullOrWhiteSpace(observed.RunId))
            _bufferedRunId = observed.RunId;

        RefreshBufferedRunContext();
    }

    public static bool TryConsumeContinuePassthrough()
    {
        return _current?.ConsumeContinuePassthrough() == true;
    }

    public static bool ShouldSuppressContinueWhileCaptureInFlight()
    {
        return _current?.ShouldBlockMouseInput() == true;
    }

    public static void NotifyBlockerClick()
    {
        _current?.HandleBlockerClick();
    }

    public static bool TryCaptureFirstContinue(
        EndOfRunScreenController controller,
        bool isInteractionBlocked
    )
    {
        return _current?.CaptureFirstContinue(controller, isInteractionBlocked) == true;
    }

    public static bool ShouldBlockContinueUntilFirstCapture(EndOfRunScreenController controller)
    {
        return _current?.ShouldBlockContinueUntilFirstCaptureInternal(controller) == true;
    }

    private bool ConsumeContinuePassthrough()
    {
        return _gate.ConsumeContinuePassthrough();
    }

    private bool ShouldBlockMouseInput()
    {
        return _gate.IsAttemptInFlight();
    }

    private bool ShouldBlockContinueUntilFirstCaptureInternal(EndOfRunScreenController controller)
    {
        if (_screenshotService == null)
            return false;

        TrackEndOfRunEntry(controller);
        return ShouldHoldBeforeFirstCapture(out _);
    }

    private void HandleBlockerClick()
    {
        var controller = FindActiveEndOfRunScreenController();
        if (controller == null)
            return;

        TrackEndOfRunEntry(controller);

        if (_gate.HasCapturedForCurrentRun() || ShouldHoldBeforeFirstCapture(out _))
            return;

        _ = CaptureFirstContinue(controller, isInteractionBlocked: false);
    }

    private bool CaptureFirstContinue(
        EndOfRunScreenController controller,
        bool isInteractionBlocked
    )
    {
        if (
            _screenshotService == null
            || !_gate.ShouldCaptureOnContinue(isInteractionBlocked, Time.unscaledTime)
        )
            return false;

        if (_captureCoroutine != null)
            return false;

        var activeController = FindActiveEndOfRunScreenController();
        if (activeController != null)
            _mouseBlocker.Attach(activeController);

        BppLog.Info(
            "EndOfRunScreenshot",
            $"CaptureFirstContinue action=start frame={Time.frameCount} time={Time.unscaledTime:F3} isInteractionBlocked={isInteractionBlocked} runId={ResolveRunId() ?? "<null>"} hero={ResolveHeroName() ?? "<null>"}"
        );

        _captureCoroutine = StartCoroutine(CaptureAndContinue(controller));
        return true;
    }

    private IEnumerator CaptureAndContinue(EndOfRunScreenController controller)
    {
        ScreenshotCaptureResult? capture = null;
        var shouldPassthrough = false;
        try
        {
            _captureSuppressionScope = BeginUiSuppression();
            yield return new WaitForEndOfFrame();

            try
            {
                capture = _screenshotService?.CaptureCurrentFrame(
                    new ScreenshotCaptureRequest
                    {
                        RunId = ResolveRunId(),
                        HeroName = ResolveHeroName(),
                        CaptureSource = RunScreenshotCaptureSource.EndOfRunAuto,
                    }
                );
                if (capture != null)
                {
                    PersistCapture(capture, isPrimary: true);
                    _gate.MarkAttemptCompleted();
                    shouldPassthrough = true;
                    BppLog.Info(
                        "EndOfRunScreenshot",
                        $"CaptureCoroutine action=captured frame={Time.frameCount} time={Time.unscaledTime:F3}"
                    );
                }
                else
                {
                    _gate.AbortCaptureAttempt(Time.unscaledTime + CaptureRetryCooldownSeconds);
                    BppLog.Warn(
                        "EndOfRunScreenshot",
                        "Screenshot attempt aborted before it could be queued."
                    );
                }
            }
            catch (Exception ex)
            {
                _gate.AbortCaptureAttempt(Time.unscaledTime + CaptureRetryCooldownSeconds);
                BppLog.Error("EndOfRunScreenshot", "End-of-run screenshot capture failed.", ex);
            }

            if (shouldPassthrough)
            {
                yield return null;
                _gate.AllowNextContinuePassthrough();
                BppLog.Info(
                    "EndOfRunScreenshot",
                    $"CaptureCoroutine action=invoke-continue frame={Time.frameCount} time={Time.unscaledTime:F3}"
                );
                ContinueClickMethod.Invoke(controller, []);
            }
        }
        finally
        {
            _captureCoroutine = null;
            DisposeCaptureSuppressionScope();
            if (_gate.IsAttemptInFlight() && !shouldPassthrough)
                _gate.AbortCaptureAttempt(Time.unscaledTime + CaptureRetryCooldownSeconds);
            _mouseBlocker.Detach();
        }
    }

    private void PersistCapture(ScreenshotCaptureResult? capture, bool isPrimary = false)
    {
        if (capture == null || _screenshotStore == null)
            return;

        try
        {
            _screenshotStore.Save(RunScreenshotMetadataReader.CreateRecord(capture, isPrimary));
        }
        catch (Exception ex)
        {
            BppLog.Error("ScreenshotService", "Failed to persist screenshot metadata.", ex);
        }
    }

    private string? ResolveRunId()
    {
        RefreshBufferedRunContext();
        return _bufferedRunId;
    }

    private string? ResolveHeroName()
    {
        RefreshBufferedRunContext();
        return _bufferedHeroName;
    }

    private void ResetBufferedRunContext()
    {
        _bufferedRunId = null;
        _bufferedHeroName = null;
    }

    private void RefreshBufferedRunContext()
    {
        if (_services != null)
        {
            var liveRunId = _services.RunContext.CurrentServerRunId;
            if (!string.IsNullOrWhiteSpace(liveRunId))
                _bufferedRunId = liveRunId;
        }

        var liveHeroName = Data.Run?.Player?.Hero.ToString();
        if (!string.IsNullOrWhiteSpace(liveHeroName))
            _bufferedHeroName = liveHeroName;
    }

    private static IDisposable? BeginUiSuppression()
    {
        return ScreenshotUiSuppressionScope.Begin(
            BppSettingsDockController.BeginScreenshotSuppression,
            CombatStatusBarFeature.BeginScreenshotSuppression
        );
    }

    private void DisposeCaptureSuppressionScope()
    {
        _captureSuppressionScope?.Dispose();
        _captureSuppressionScope = null;
    }

    private void ResetCaptureUiState()
    {
        if (_captureCoroutine != null)
        {
            StopCoroutine(_captureCoroutine);
            _captureCoroutine = null;
        }

        DisposeCaptureSuppressionScope();
        _gate.CancelCaptureAttempt();
        _mouseBlocker.Destroy();
        _trackedEndOfRunControllerId = 0;
        _endOfRunEnteredAtSeconds = -1f;
    }

    private static EndOfRunScreenController? FindActiveEndOfRunScreenController()
    {
        var controllers = UnityEngine.Object.FindObjectsOfType<EndOfRunScreenController>(
            includeInactive: true
        );
        foreach (var controller in controllers)
        {
            if (controller == null)
                continue;

            if (controller.gameObject.activeInHierarchy)
                return controller;
        }

        return null;
    }

    private void SyncEndOfRunMouseBlocker()
    {
        if (_screenshotService == null)
        {
            _mouseBlocker.Detach();
            return;
        }

        var screenController = FindActiveEndOfRunScreenController();
        if (screenController == null)
        {
            _mouseBlocker.Detach();
            _trackedEndOfRunControllerId = 0;
            _endOfRunEnteredAtSeconds = -1f;
            return;
        }

        TrackEndOfRunEntry(screenController);
        var shouldShowBlocker =
            ShouldHoldBeforeFirstCapture(out var holdReason) || ShouldBlockMouseInput();

        LogBlockerStateChange(
            isActive: shouldShowBlocker,
            shouldShowBlocker
                ? (
                    ShouldBlockMouseInput()
                        ? $"reason=capture-in-flight {holdReason}"
                        : $"reason={(holdReason.StartsWith("reason=wait-for-first-capture-window", StringComparison.Ordinal) ? "first-capture-window-blocked" : "continue-blocked")} {holdReason}"
                )
                : $"reason=continue-unblocked {holdReason}"
        );
        if (shouldShowBlocker)
            _mouseBlocker.Attach(screenController);
        else
            _mouseBlocker.Detach();
    }

    private void LogBlockerStateChange(bool isActive, string stateSummary)
    {
        if (
            _lastLoggedBlockerActive == isActive
            && string.Equals(_lastLoggedBlockerStateSummary, stateSummary, StringComparison.Ordinal)
        )
            return;

        _lastLoggedBlockerActive = isActive;
        _lastLoggedBlockerStateSummary = stateSummary;
        BppLog.Info(
            "EndOfRunScreenshot",
            $"BlockerState active={isActive} frame={Time.frameCount} time={Time.unscaledTime:F3} {stateSummary}"
        );
    }

    private bool ShouldHoldBeforeFirstCapture(out string holdReason)
    {
        if (_gate.HasCapturedForCurrentRun())
        {
            holdReason = "reason=already-captured";
            return false;
        }

        if (_endOfRunEnteredAtSeconds < 0f)
        {
            holdReason = "reason=awaiting-end-of-run-entry";
            return true;
        }

        var elapsedSeconds = Time.unscaledTime - _endOfRunEnteredAtSeconds;
        if (elapsedSeconds >= FirstCaptureDelaySeconds)
        {
            holdReason =
                $"reason=first-capture-window-open elapsed={elapsedSeconds:F3} delay={FirstCaptureDelaySeconds:F3}";
            return false;
        }

        holdReason =
            $"reason=wait-for-first-capture-window elapsed={elapsedSeconds:F3} delay={FirstCaptureDelaySeconds:F3} remaining={FirstCaptureDelaySeconds - elapsedSeconds:F3}";
        return true;
    }

    private void TrackEndOfRunEntry(EndOfRunScreenController controller)
    {
        var controllerId = controller.GetInstanceID();
        if (_trackedEndOfRunControllerId == controllerId && _endOfRunEnteredAtSeconds >= 0f)
            return;

        _trackedEndOfRunControllerId = controllerId;
        _endOfRunEnteredAtSeconds = Time.unscaledTime;
        BppLog.Info(
            "EndOfRunScreenshot",
            $"EndOfRunEntry frame={Time.frameCount} time={Time.unscaledTime:F3} controller={controller.name} delay={FirstCaptureDelaySeconds:F3}"
        );
    }
}
