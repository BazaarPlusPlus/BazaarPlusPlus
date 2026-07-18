#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunScreenshot;

namespace BazaarPlusPlus.Game.Screenshots;

internal readonly record struct EndOfRunCaptureContext(
    bool Enabled,
    string? RunId,
    string? HeroName
);

internal readonly record struct EndOfRunCapturePolicy(
    float RetryCooldownSeconds,
    float CaptureTimeoutSeconds,
    float MetadataTimeoutSeconds,
    float RevealDeadlineSeconds,
    int MaxAttempts
)
{
    internal static EndOfRunCapturePolicy Default => new(1f, 15f, 5f, 20f, 2);
}

internal readonly record struct EndOfRunCaptureAttemptOutcome(
    ScreenshotCaptureResult? Capture,
    ScreenshotCaptureReasonCode? FailureReason,
    Exception? Exception
)
{
    internal static EndOfRunCaptureAttemptOutcome Succeeded(ScreenshotCaptureResult capture) =>
        new(capture ?? throw new ArgumentNullException(nameof(capture)), null, null);

    internal static EndOfRunCaptureAttemptOutcome Failed(
        ScreenshotCaptureReasonCode reason,
        Exception? exception = null
    ) => new(null, reason, exception);
}

internal interface IEndOfRunCaptureAttempt
{
    bool HasCaptureStarted { get; }
    Task<EndOfRunCaptureAttemptOutcome> Completion { get; }
    void RestoreUi();
    void Cancel();
}

internal interface IEndOfRunCaptureSurface<TScreen>
    where TScreen : class
{
    bool IsAvailable { get; }
    TScreen? FindActiveScreen();
    bool IsScreenActive(TScreen screen);
    int GetScreenId(TScreen screen);
    EndOfRunCaptureReadinessOutcome GetReadiness(TScreen screen, bool hasRevealStarted);
    IEndOfRunCaptureAttempt BeginCapture(TScreen screen, ScreenshotCaptureRequest request);
    void SetContinueBlocked(TScreen? screen, bool blocked);
}

internal interface IEndOfRunArtifactPersistence
{
    Task<ScreenshotMetadataPersistenceOutcome> PersistAsync(
        ScreenshotCaptureResult capture,
        bool isPrimary
    );
}

internal interface IEndOfRunCaptureClock
{
    float UnscaledSeconds { get; }
    float RealtimeSeconds { get; }
    long Milliseconds { get; }
}

internal interface IEndOfRunCaptureFileSystem
{
    bool IsUsablePng(string? filePath);
    void DeleteIfExists(string? filePath);
}

internal sealed class EndOfRunCaptureWorkflowCore<TScreen>
    where TScreen : class
{
    private readonly IEndOfRunArtifactPersistence _persistence;
    private readonly IEndOfRunCaptureClock _clock;
    private readonly IEndOfRunCaptureFileSystem _files;
    private readonly EndOfRunCapturePolicy _policy;
    private RunState _state = new(generation: 0);
    private IEndOfRunCaptureSurface<TScreen>? _surface;

    internal EndOfRunCaptureWorkflowCore(
        IEndOfRunArtifactPersistence persistence,
        IEndOfRunCaptureClock clock,
        IEndOfRunCaptureFileSystem files,
        EndOfRunCapturePolicy policy
    )
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _policy = policy.MaxAttempts > 0 ? policy : EndOfRunCapturePolicy.Default;
    }

    internal void AttachSurface(IEndOfRunCaptureSurface<TScreen> surface)
    {
        if (surface == null)
            throw new ArgumentNullException(nameof(surface));
        if (_surface != null && !ReferenceEquals(_surface, surface))
            DetachSurface(_surface);
        _surface = surface;
    }

    internal void DetachSurface(IEndOfRunCaptureSurface<TScreen> surface)
    {
        if (!ReferenceEquals(_surface, surface))
            return;

        CompleteContextExpired();
        SetContinueBlocked(screen: null, blocked: false);
        _surface = null;
    }

    internal void OnRunStarted()
    {
        if (!_state.Terminal && _state.Operation != null)
            CompleteContextExpired();
        else
            CancelOutstandingWork(cleanLateArtifact: true);
        SetContinueBlocked(screen: null, blocked: false);
        _state = new RunState(_state.Generation + 1);
    }

    internal void OnEndOfRunInitializing()
    {
        if (_state.Terminal)
            return;
        if (_state.ActiveAttempt != null || _state.PersistenceTask != null)
        {
            CompleteContextExpired();
            return;
        }

        _state.Armed = true;
        _state.Stage = WorkflowStage.WaitingForReadiness;
        _state.SummaryObserved = false;
        _state.RevealStarted = false;
        _state.RevealDeadline = null;
        _state.RetryAvailableAt = 0f;
    }

    internal void ObserveRevealStarted(TScreen screen)
    {
        var surface = _surface;
        if (surface == null || screen == null || !surface.IsScreenActive(screen) || _state.Terminal)
            return;
        if (!TryTrackScreen(surface, screen))
            return;

        _state.Armed = true;
        _state.RevealStarted = true;
        _state.RevealDeadline = null;
        if (_state.Stage == WorkflowStage.Idle)
            _state.Stage = WorkflowStage.WaitingForReadiness;
    }

    internal void OnFrame(EndOfRunCaptureContext context)
    {
        var surface = _surface;
        if (surface == null || !surface.IsAvailable || !context.Enabled)
        {
            if (_state.ActiveAttempt != null || _state.PersistenceTask != null)
                CompleteContextExpired();
            else
                SetContinueBlocked(screen: null, blocked: false);
            return;
        }

        var screen = surface.FindActiveScreen();
        if (screen == null || !surface.IsScreenActive(screen))
        {
            if (_state.ScreenId != 0)
                CompleteContextExpired();
            else
                SetContinueBlocked(screen: null, blocked: false);
            return;
        }

        ProcessScreen(surface, screen, context, canStartCapture: true);
    }

    internal bool ShouldBlockContinue(TScreen screen, EndOfRunCaptureContext context)
    {
        var surface = _surface;
        if (
            surface == null
            || !surface.IsAvailable
            || !context.Enabled
            || screen == null
            || !surface.IsScreenActive(screen)
        )
        {
            SetContinueBlocked(screen: null, blocked: false);
            return false;
        }

        ProcessScreen(surface, screen, context, canStartCapture: false);
        return _state.ContinueBlocked;
    }

    internal void FailOpenUnexpected(Exception exception)
    {
        if (_state.Terminal)
            return;
        EnsureOperation(new EndOfRunCaptureContext(true, RunId: null, HeroName: null));
        CancelOutstandingWork(cleanLateArtifact: true);
        var filePath = _state.Operation?.VerifiedArtifactPath;
        CompleteTerminal(
            string.IsNullOrWhiteSpace(filePath)
                ? CaptureTerminalKind.Failed
                : CaptureTerminalKind.Degraded,
            ScreenshotCaptureReasonCode.ContextExpired,
            string.IsNullOrWhiteSpace(filePath)
                ? ScreenshotArtifactStatus.Unavailable
                : ScreenshotArtifactStatus.MetadataPending,
            filePath,
            exception
        );
    }

    private void ProcessScreen(
        IEndOfRunCaptureSurface<TScreen> surface,
        TScreen screen,
        EndOfRunCaptureContext context,
        bool canStartCapture
    )
    {
        if (_state.Terminal)
        {
            SetContinueBlocked(screen: null, blocked: false);
            return;
        }
        if (!TryTrackScreen(surface, screen))
            return;
        if (!_state.Armed)
            OnEndOfRunInitializing();

        if (_state.Stage == WorkflowStage.Capturing)
        {
            PollCapture(screen, context);
            return;
        }
        if (_state.Stage == WorkflowStage.Persisting)
        {
            PollPersistence();
            return;
        }

        var readiness = surface.GetReadiness(screen, _state.RevealStarted);
        if (readiness.State != EndOfRunCaptureReadinessState.NotSummary)
            EnsureOperation(context);

        switch (readiness.State)
        {
            case EndOfRunCaptureReadinessState.UnknownTarget:
                CompleteTerminal(
                    CaptureTerminalKind.Failed,
                    readiness.ReasonCode ?? ScreenshotCaptureReasonCode.RevealProbeFailed,
                    ScreenshotArtifactStatus.Unavailable,
                    filePath: null,
                    readiness.Exception
                );
                return;
            case EndOfRunCaptureReadinessState.NotSummary:
                if (_state.SummaryObserved)
                    CompleteContextExpired();
                else
                    SetContinueBlocked(screen: null, blocked: false);
                return;
            case EndOfRunCaptureReadinessState.TransitionInProgress:
                _state.SummaryObserved = true;
                SetContinueBlocked(screen: null, blocked: false);
                return;
            case EndOfRunCaptureReadinessState.RevealNotStarted:
            case EndOfRunCaptureReadinessState.RevealInProgress:
            case EndOfRunCaptureReadinessState.DetectionFailed:
                _state.SummaryObserved = true;
                _state.RevealDeadline ??=
                    _clock.UnscaledSeconds + Math.Max(0f, _policy.RevealDeadlineSeconds);
                if (_clock.UnscaledSeconds < _state.RevealDeadline.Value)
                {
                    SetContinueBlocked(screen, blocked: true);
                    return;
                }

                if (readiness.State != EndOfRunCaptureReadinessState.DetectionFailed)
                {
                    CompleteTerminal(
                        CaptureTerminalKind.Failed,
                        ScreenshotCaptureReasonCode.ReadinessDeadline,
                        ScreenshotArtifactStatus.Unavailable,
                        filePath: null,
                        readiness.Exception
                    );
                    return;
                }

                _state.DegradationReason ??=
                    readiness.ReasonCode ?? ScreenshotCaptureReasonCode.ReadinessDeadline;
                _state.DegradationException ??= readiness.Exception;
                break;
            case EndOfRunCaptureReadinessState.Ready:
                _state.SummaryObserved = true;
                break;
            default:
                SetContinueBlocked(screen: null, blocked: false);
                return;
        }

        if (_state.Stage == WorkflowStage.WaitingForRetry)
        {
            if (_clock.UnscaledSeconds < _state.RetryAvailableAt)
            {
                SetContinueBlocked(screen, blocked: true);
                return;
            }
            _state.Stage = WorkflowStage.WaitingForReadiness;
        }

        SetContinueBlocked(screen, blocked: true);
        if (canStartCapture)
            BeginCapture(surface, screen, context);
    }

    private void BeginCapture(
        IEndOfRunCaptureSurface<TScreen> surface,
        TScreen screen,
        EndOfRunCaptureContext context
    )
    {
        if (_state.ActiveAttempt != null || _state.Terminal)
            return;
        var operation = EnsureOperation(context);
        operation.AttemptCount++;
        var request = new ScreenshotCaptureRequest
        {
            ScreenshotId = operation.ScreenshotId,
            RunId = operation.RunId,
            HeroName = context.HeroName,
            CaptureSource = RunScreenshotCaptureSource.EndOfRunAuto,
        };

        try
        {
            _state.ActiveAttempt = surface.BeginCapture(screen, request);
            if (_state.ActiveAttempt == null)
            {
                HandleAttemptFailure(
                    ScreenshotCaptureReasonCode.CaptureReturnedNull,
                    exception: null
                );
                return;
            }
        }
        catch (Exception ex)
        {
            HandleAttemptFailure(ScreenshotCaptureReasonCode.CaptureSynchronousException, ex);
            return;
        }

        _state.Stage = WorkflowStage.Capturing;
        _state.CaptureDeadline = _state.ActiveAttempt.HasCaptureStarted
            ? _clock.RealtimeSeconds + Math.Max(0f, _policy.CaptureTimeoutSeconds)
            : null;
    }

    private void PollCapture(TScreen screen, EndOfRunCaptureContext context)
    {
        var surface = _surface;
        var attempt = _state.ActiveAttempt;
        if (surface == null || attempt == null || !surface.IsScreenActive(screen))
        {
            CompleteContextExpired();
            return;
        }

        var completion = attempt.Completion;
        if (!completion.IsCompleted)
        {
            if (!attempt.HasCaptureStarted)
            {
                SetContinueBlocked(screen, blocked: true);
                return;
            }

            _state.CaptureDeadline ??=
                _clock.RealtimeSeconds + Math.Max(0f, _policy.CaptureTimeoutSeconds);
            if (_clock.RealtimeSeconds < _state.CaptureDeadline.Value)
            {
                SetContinueBlocked(screen, blocked: true);
                return;
            }

            attempt.Cancel();
            ObserveLateCapture(completion);
            _state.ActiveAttempt = null;
            CompleteTerminal(
                CaptureTerminalKind.Failed,
                ScreenshotCaptureReasonCode.CaptureTimeout,
                ScreenshotArtifactStatus.Unavailable,
                filePath: null,
                exception: null
            );
            return;
        }

        EndOfRunCaptureAttemptOutcome outcome;
        try
        {
            outcome = completion.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            if (TryRestoreUi(attempt))
                HandleAttemptFailure(ScreenshotCaptureReasonCode.CaptureTaskFaulted, ex);
            return;
        }

        if (outcome.FailureReason.HasValue)
        {
            if (TryRestoreUi(attempt))
                HandleAttemptFailure(outcome.FailureReason.Value, outcome.Exception);
            return;
        }
        if (outcome.Capture == null)
        {
            if (TryRestoreUi(attempt))
                HandleAttemptFailure(ScreenshotCaptureReasonCode.CaptureReturnedNull, null);
            return;
        }
        if (!IsUsablePng(outcome.Capture.FilePath))
        {
            if (TryRestoreUi(attempt))
                HandleAttemptFailure(ScreenshotCaptureReasonCode.CaptureArtifactUnavailable, null);
            return;
        }

        var operation = EnsureOperation(context);
        operation.VerifiedArtifactPath = outcome.Capture.FilePath;
        if (!TryRestoreUi(attempt))
            return;
        _state.ActiveAttempt = null;
        StartPersistence(outcome.Capture);
    }

    private void StartPersistence(ScreenshotCaptureResult capture)
    {
        try
        {
            _state.PersistenceTask = _persistence.PersistAsync(capture, isPrimary: true);
        }
        catch (Exception ex)
        {
            CompleteTerminal(
                CaptureTerminalKind.Degraded,
                ScreenshotCaptureReasonCode.MetadataFailed,
                ScreenshotArtifactStatus.FileOnly,
                capture.FilePath,
                ex
            );
            return;
        }

        if (_state.PersistenceTask == null)
        {
            CompleteTerminal(
                CaptureTerminalKind.Degraded,
                ScreenshotCaptureReasonCode.MetadataUnavailable,
                ScreenshotArtifactStatus.FileOnly,
                capture.FilePath,
                exception: null
            );
            return;
        }

        _state.Stage = WorkflowStage.Persisting;
        _state.MetadataDeadline =
            _clock.RealtimeSeconds + Math.Max(0f, _policy.MetadataTimeoutSeconds);
        PollPersistence();
    }

    private void PollPersistence()
    {
        var persistence = _state.PersistenceTask;
        var filePath = _state.Operation?.VerifiedArtifactPath;
        if (persistence == null)
        {
            CompleteTerminal(
                CaptureTerminalKind.Degraded,
                ScreenshotCaptureReasonCode.MetadataUnavailable,
                ScreenshotArtifactStatus.FileOnly,
                filePath,
                exception: null
            );
            return;
        }

        if (!persistence.IsCompleted)
        {
            if (_clock.RealtimeSeconds < _state.MetadataDeadline)
            {
                SetContinueBlocked(_state.Screen, blocked: true);
                return;
            }

            ObserveLatePersistence(persistence);
            _state.PersistenceTask = null;
            CompleteTerminal(
                CaptureTerminalKind.Degraded,
                ScreenshotCaptureReasonCode.MetadataTimeout,
                ScreenshotArtifactStatus.MetadataPending,
                filePath,
                exception: null
            );
            return;
        }

        _state.PersistenceTask = null;
        ScreenshotMetadataPersistenceOutcome outcome;
        try
        {
            outcome = persistence.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            outcome = ScreenshotMetadataPersistenceOutcome.Failed(ex);
        }

        switch (outcome.Status)
        {
            case ScreenshotMetadataPersistenceStatus.Failed:
                CompleteTerminal(
                    CaptureTerminalKind.Degraded,
                    ScreenshotCaptureReasonCode.MetadataFailed,
                    ScreenshotArtifactStatus.FileOnly,
                    filePath,
                    outcome.Exception
                );
                break;
            case ScreenshotMetadataPersistenceStatus.TimedOut:
                CompleteTerminal(
                    CaptureTerminalKind.Degraded,
                    ScreenshotCaptureReasonCode.MetadataTimeout,
                    ScreenshotArtifactStatus.MetadataPending,
                    filePath,
                    exception: null
                );
                break;
            case ScreenshotMetadataPersistenceStatus.Unavailable:
                CompleteTerminal(
                    CaptureTerminalKind.Degraded,
                    ScreenshotCaptureReasonCode.MetadataUnavailable,
                    ScreenshotArtifactStatus.FileOnly,
                    filePath,
                    exception: null
                );
                break;
            default:
                CompleteTerminal(
                    _state.DegradationReason.HasValue
                        ? CaptureTerminalKind.Degraded
                        : CaptureTerminalKind.Succeeded,
                    _state.DegradationReason ?? ScreenshotCaptureReasonCode.Completed,
                    ScreenshotArtifactStatus.Complete,
                    filePath,
                    _state.DegradationException
                );
                break;
        }
    }

    private bool TryRestoreUi(IEndOfRunCaptureAttempt attempt)
    {
        try
        {
            attempt.RestoreUi();
            return true;
        }
        catch (Exception ex)
        {
            FailOpenUnexpected(ex);
            return false;
        }
    }

    private void HandleAttemptFailure(ScreenshotCaptureReasonCode reason, Exception? exception)
    {
        _state.ActiveAttempt = null;
        var attempts = _state.Operation?.AttemptCount ?? _policy.MaxAttempts;
        if (IsRetryableCaptureFailure(reason) && attempts < _policy.MaxAttempts)
        {
            _state.Stage = WorkflowStage.WaitingForRetry;
            _state.RetryAvailableAt =
                _clock.UnscaledSeconds + Math.Max(0f, _policy.RetryCooldownSeconds);
            SetContinueBlocked(_state.Screen, blocked: true);
            return;
        }

        CompleteTerminal(
            CaptureTerminalKind.Failed,
            reason,
            ScreenshotArtifactStatus.Unavailable,
            filePath: null,
            exception
        );
    }

    private static bool IsRetryableCaptureFailure(ScreenshotCaptureReasonCode reason) =>
        reason
            is ScreenshotCaptureReasonCode.CaptureSynchronousException
                or ScreenshotCaptureReasonCode.CaptureTaskFaulted
                or ScreenshotCaptureReasonCode.CaptureReturnedNull
                or ScreenshotCaptureReasonCode.CaptureArtifactUnavailable;

    private bool TryTrackScreen(IEndOfRunCaptureSurface<TScreen> surface, TScreen screen)
    {
        var screenId = surface.GetScreenId(screen);
        if (_state.ScreenId != 0 && _state.ScreenId != screenId)
        {
            CompleteContextExpired();
            return false;
        }

        _state.ScreenId = screenId;
        _state.Screen = screen;
        return true;
    }

    private CaptureOperationState EnsureOperation(EndOfRunCaptureContext context)
    {
        return _state.Operation ??= new CaptureOperationState(
            Guid.NewGuid().ToString("N"),
            context.RunId,
            _clock.Milliseconds
        );
    }

    private void CompleteContextExpired()
    {
        if (_state.Terminal)
        {
            SetContinueBlocked(screen: null, blocked: false);
            return;
        }

        CancelOutstandingWork(cleanLateArtifact: true);
        var operation = _state.Operation;
        if (operation == null)
        {
            _state.Terminal = true;
            _state.Stage = WorkflowStage.Terminal;
            SetContinueBlocked(screen: null, blocked: false);
            return;
        }

        var filePath = operation.VerifiedArtifactPath;
        CompleteTerminal(
            string.IsNullOrWhiteSpace(filePath)
                ? CaptureTerminalKind.Failed
                : CaptureTerminalKind.Degraded,
            ScreenshotCaptureReasonCode.ContextExpired,
            string.IsNullOrWhiteSpace(filePath)
                ? ScreenshotArtifactStatus.Unavailable
                : ScreenshotArtifactStatus.MetadataPending,
            filePath,
            exception: null
        );
    }

    private void CompleteTerminal(
        CaptureTerminalKind kind,
        ScreenshotCaptureReasonCode reason,
        ScreenshotArtifactStatus artifactStatus,
        string? filePath,
        Exception? exception
    )
    {
        if (_state.Terminal)
            return;

        _state.Terminal = true;
        _state.Armed = false;
        _state.Stage = WorkflowStage.Terminal;
        _state.ActiveAttempt = null;
        _state.PersistenceTask = null;
        SetContinueBlocked(screen: null, blocked: false);

        var operation = _state.Operation;
        if (operation == null)
            return;
        ReportTerminal(operation, kind, reason, artifactStatus, filePath, exception);
    }

    private void CancelOutstandingWork(bool cleanLateArtifact)
    {
        var attempt = _state.ActiveAttempt;
        _state.ActiveAttempt = null;
        if (attempt != null)
        {
            try
            {
                attempt.Cancel();
            }
            catch
            {
                // Cancellation is best-effort; the workflow still fails open.
            }
            if (cleanLateArtifact)
            {
                try
                {
                    ObserveLateCapture(attempt.Completion);
                }
                catch
                {
                    // A broken adapter cannot prevent terminal fail-open cleanup.
                }
            }
        }

        var persistence = _state.PersistenceTask;
        _state.PersistenceTask = null;
        if (persistence != null)
            ObserveLatePersistence(persistence);
    }

    private void ObserveLateCapture(Task<EndOfRunCaptureAttemptOutcome> completion)
    {
        var screenshotId = _state.Operation?.ScreenshotId;
        _ = completion.ContinueWith(
            task =>
            {
                try
                {
                    if (
                        task.Status == TaskStatus.RanToCompletion
                        && task.Result.Capture is { FilePath: { Length: > 0 } file }
                    )
                        _files.DeleteIfExists(file);
                    else if (task.IsFaulted)
                        _ = task.Exception;
                }
                catch (Exception ex)
                {
                    ScreenshotCaptureDiagnostics.ReportCleanupFailed(
                        ScreenshotCaptureCleanupStage.LateFileDelete,
                        screenshotId,
                        task.Status == TaskStatus.RanToCompletion
                            ? task.Result.Capture?.FilePath
                            : null,
                        ex
                    );
                }
            },
            TaskScheduler.Default
        );
    }

    private static void ObserveLatePersistence(
        Task<ScreenshotMetadataPersistenceOutcome> persistence
    )
    {
        _ = persistence.ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                    _ = task.Exception;
                else if (task.Status == TaskStatus.RanToCompletion)
                    _ = task.Result;
            },
            TaskScheduler.Default
        );
    }

    private bool IsUsablePng(string? filePath)
    {
        try
        {
            return _files.IsUsablePng(filePath);
        }
        catch
        {
            return false;
        }
    }

    private void SetContinueBlocked(TScreen? screen, bool blocked)
    {
        if (blocked && _state.ContinueBlocked)
            return;

        _state.ContinueBlocked = blocked;
        try
        {
            _surface?.SetContinueBlocked(blocked ? screen : null, blocked);
        }
        catch
        {
            _state.ContinueBlocked = false;
        }
    }

    private void ReportTerminal(
        CaptureOperationState operation,
        CaptureTerminalKind kind,
        ScreenshotCaptureReasonCode reason,
        ScreenshotArtifactStatus artifactStatus,
        string? filePath,
        Exception? exception
    )
    {
        try
        {
            var fields = new[]
            {
                ScreenshotCaptureLogEvents.ScreenshotId.Bind(operation.ScreenshotId),
                ScreenshotCaptureLogEvents.RunId.Bind(operation.RunId),
                ScreenshotCaptureLogEvents.CaptureSource.Bind(
                    RunScreenshotCaptureSource.EndOfRunAuto
                ),
                ScreenshotCaptureLogEvents.ReasonCode.Bind(reason),
                ScreenshotCaptureLogEvents.ArtifactStatus.Bind(artifactStatus),
                ScreenshotCaptureLogEvents.AttemptCount.Bind(operation.AttemptCount),
                ScreenshotCaptureLogEvents.DurationMs.Bind(
                    Math.Max(0, _clock.Milliseconds - operation.StartedAtMilliseconds)
                ),
                ScreenshotCaptureLogEvents.FilePath.Bind(filePath),
            };
            var definition = kind switch
            {
                CaptureTerminalKind.Succeeded => ScreenshotCaptureLogEvents.CaptureSucceeded,
                CaptureTerminalKind.Degraded => ScreenshotCaptureLogEvents.CaptureDegraded,
                _ => ScreenshotCaptureLogEvents.CaptureFailed,
            };
            if (kind == CaptureTerminalKind.Succeeded)
                BppLog.InfoEvent(definition, fields);
            else if (kind == CaptureTerminalKind.Degraded)
            {
                if (exception == null)
                    BppLog.WarnEvent(definition, fields);
                else
                    BppLog.WarnEvent(definition, exception, fields);
            }
            else if (exception == null)
                BppLog.ErrorEvent(definition, fields);
            else
                BppLog.ErrorEvent(definition, exception, fields);
        }
        catch
        {
            // Logging cannot prevent fail-open or create a second terminal outcome.
        }
    }

    private sealed class RunState
    {
        internal RunState(int generation) => Generation = generation;

        internal int Generation { get; }
        internal bool Armed { get; set; }
        internal bool Terminal { get; set; }
        internal bool SummaryObserved { get; set; }
        internal bool RevealStarted { get; set; }
        internal bool ContinueBlocked { get; set; }
        internal int ScreenId { get; set; }
        internal TScreen? Screen { get; set; }
        internal WorkflowStage Stage { get; set; }
        internal float? RevealDeadline { get; set; }
        internal float RetryAvailableAt { get; set; }
        internal float? CaptureDeadline { get; set; }
        internal float MetadataDeadline { get; set; }
        internal IEndOfRunCaptureAttempt? ActiveAttempt { get; set; }
        internal Task<ScreenshotMetadataPersistenceOutcome>? PersistenceTask { get; set; }
        internal CaptureOperationState? Operation { get; set; }
        internal ScreenshotCaptureReasonCode? DegradationReason { get; set; }
        internal Exception? DegradationException { get; set; }
    }

    private sealed class CaptureOperationState
    {
        internal CaptureOperationState(
            string screenshotId,
            string? runId,
            long startedAtMilliseconds
        )
        {
            ScreenshotId = screenshotId;
            RunId = runId;
            StartedAtMilliseconds = startedAtMilliseconds;
        }

        internal string ScreenshotId { get; }
        internal string? RunId { get; }
        internal long StartedAtMilliseconds { get; }
        internal int AttemptCount { get; set; }
        internal string? VerifiedArtifactPath { get; set; }
    }

    private enum WorkflowStage
    {
        Idle,
        WaitingForReadiness,
        WaitingForRetry,
        Capturing,
        Persisting,
        Terminal,
    }

    private enum CaptureTerminalKind
    {
        Succeeded,
        Degraded,
        Failed,
    }
}
