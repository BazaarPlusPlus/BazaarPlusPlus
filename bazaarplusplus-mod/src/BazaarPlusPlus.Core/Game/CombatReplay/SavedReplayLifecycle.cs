#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>
/// Pure owner of a saved-replay playback session's state algebra: start progress, terminal
/// ownership, the time-bounded duplicate-exit suppression window, bootstrap/menu-return flags,
/// startup-interruption latch, and the pending menu-return deadline. The runtime feeds
/// observations and executes the returned decisions. It never exits <c>ReplayState</c> itself:
/// per ADR-0002 the native recap continue click and the bootstrapped <c>Exit()</c> prefix are the
/// only exit paths.
/// </summary>
/// <remarks>
/// Zero Unity / zero held delegates. All time is a caller-supplied <c>float now</c>
/// (typically <c>Time.realtimeSinceStartup</c>).
/// </remarks>
public sealed class SavedReplayLifecycle
{
    /// <summary>
    /// Escape-hatch window after a bootstrapped exit is latched. ReturnToMainMenu awaits a
    /// network call internally and can silently fail, leaving the game parked in ReplayState
    /// forever. Past the window, a fresh native Exit() click is allowed through again — running
    /// the original Exit body is the lesser evil versus a permanently dead continue button.
    /// </summary>
    public const float ExitSuppressionWindowSeconds = 15f;

    private enum Progress
    {
        Idle,
        StartInProgress,
        SavedPlaybackActive,
        StartFailureCleanup,
    }

    private Progress _progress;
    private bool _returnToMenuAfterReplay;
    private bool _bootstrappedReplayActive;
    private bool _replayExitInProgress;
    private float _replayExitRequestedAt;
    private ReplayPlaybackReasonCode _startupInterruptionReason;
    private Exception? _startupInterruptionException;
    private bool _exitOwnsTerminalByStart;
    private bool _forceMenuReturnAfterBootstrappedExit;
    private PendingMenuReturn? _pendingMenuReturn;

    public bool IsSavedReplayPlaybackActive =>
        _progress is Progress.StartInProgress or Progress.SavedPlaybackActive;

    public bool IsReplayStartInProgress =>
        _progress is Progress.StartInProgress or Progress.StartFailureCleanup;

    // --- Start (staged commit; three distinct commit points prevent mid-start exit races) ---

    public void OnStartBegun()
    {
        _progress = Progress.StartInProgress;
        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;
        _startupInterruptionReason = ReplayPlaybackReasonCode.None;
        _startupInterruptionException = null;
        _exitOwnsTerminalByStart = false;
        _forceMenuReturnAfterBootstrappedExit = false;
    }

    public void OnBootstrapResolved(bool returnToMenuAfter)
    {
        _returnToMenuAfterReplay = returnToMenuAfter;
    }

    /// <summary>
    /// Injection succeeded and the mid-start interruption check passed. Commits
    /// <c>bootstrappedActive</c> from the already-resolved return-to-menu flag.
    /// </summary>
    public void OnInjectionCommitted()
    {
        _bootstrappedReplayActive = _returnToMenuAfterReplay;
    }

    /// <summary>
    /// Pure start-failure path (no ReplayState exit). Clears bootstrap flags and moves progress
    /// into <see cref="Progress.StartFailureCleanup"/> so the async finally can settle to Idle.
    /// </summary>
    public void OnStartFailed()
    {
        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;
        _progress = Progress.StartFailureCleanup;
        _forceMenuReturnAfterBootstrappedExit = false;
        _exitOwnsTerminalByStart = false;
    }

    public SavedReplayStartTransition OnStartFinished()
    {
        _startupInterruptionReason = ReplayPlaybackReasonCode.None;
        _startupInterruptionException = null;

        switch (_progress)
        {
            case Progress.StartInProgress:
                _progress = Progress.SavedPlaybackActive;
                return SavedReplayStartTransition.BecameActive;
            case Progress.StartFailureCleanup:
                _progress = Progress.Idle;
                return SavedReplayStartTransition.BecameIdle;
            default:
                return SavedReplayStartTransition.Unchanged;
        }
    }

    public SavedReplayStartupInterruption? TakeStartupInterruption()
    {
        if (_startupInterruptionReason == ReplayPlaybackReasonCode.None)
            return null;

        var taken = new SavedReplayStartupInterruption(
            _startupInterruptionReason,
            _startupInterruptionException
        );
        _startupInterruptionReason = ReplayPlaybackReasonCode.None;
        _startupInterruptionException = null;
        return taken;
    }

    // --- Two-phase state-exit decision (publish-before facts must not mix with publish-after) ---

    public SavedReplayExitOwnership BeginReplayStateExit(float now)
    {
        _ = now;
        var ownsTerminalByStart =
            _progress is Progress.StartInProgress or Progress.StartFailureCleanup;
        _exitOwnsTerminalByStart = ownsTerminalByStart;
        _progress = _progress switch
        {
            Progress.StartInProgress => Progress.StartFailureCleanup,
            Progress.SavedPlaybackActive => Progress.Idle,
            _ => _progress,
        };
        return new SavedReplayExitOwnership(ownsTerminalByStart);
    }

    public SavedReplayStateExitDecision OnReplayStateExited(
        float now,
        bool publishSucceeded,
        Exception? exception
    )
    {
        if (_exitOwnsTerminalByStart)
        {
            _exitOwnsTerminalByStart = false;
            var latchReason = publishSucceeded
                ? ReplayPlaybackReasonCode.StartException
                : ReplayPlaybackReasonCode.EndedPublishFailed;
            _startupInterruptionReason = latchReason;
            _startupInterruptionException = exception;
            return SavedReplayStateExitDecision.Defer(latchReason, exception);
        }

        if (_pendingMenuReturn != null)
            return SavedReplayStateExitDecision.Idle();

        var publishFailure = !publishSucceeded
            ? ReplayPlaybackReasonCode.EndedPublishFailed
            : ReplayPlaybackReasonCode.None;
        var publishException = !publishSucceeded ? exception : null;

        if (_forceMenuReturnAfterBootstrappedExit)
        {
            _forceMenuReturnAfterBootstrappedExit = false;
            ArmPendingMenuReturn(
                now,
                ReplayPlaybackEndReasonCode.SavedReplayExit,
                publishFailure,
                publishException
            );
            return SavedReplayStateExitDecision.BeginMenuReturn(
                ReplayPlaybackEndReasonCode.SavedReplayExit,
                publishFailure,
                publishException
            );
        }

        if (!_returnToMenuAfterReplay || !_bootstrappedReplayActive)
        {
            return SavedReplayStateExitDecision.CompleteNow(
                ReplayPlaybackEndReasonCode.StateExit,
                publishFailure,
                publishException
            );
        }

        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;
        ArmPendingMenuReturn(
            now,
            ReplayPlaybackEndReasonCode.StateExit,
            publishFailure,
            publishException
        );
        return SavedReplayStateExitDecision.BeginMenuReturn(
            ReplayPlaybackEndReasonCode.StateExit,
            publishFailure,
            publishException
        );
    }

    /// <summary>
    /// Synchronous menu-return dispatch failed. Clears any optimistically armed pending window
    /// so a later <see cref="TickMenuReturn"/> cannot emit CompleteTimeout for an operation the
    /// runtime already terminated with <see cref="ReplayPlaybackReasonCode.MenuReturnFailed"/>.
    /// </summary>
    public void OnMenuReturnDispatchFailed()
    {
        _pendingMenuReturn = null;
    }

    public SavedReplayMenuReturnDecision TickMenuReturn(float now, bool heroSelectLoaded)
    {
        var pending = _pendingMenuReturn;
        if (pending == null)
            return SavedReplayMenuReturnDecision.None();

        if (heroSelectLoaded)
        {
            _pendingMenuReturn = null;
            return SavedReplayMenuReturnDecision.CompleteConfirmed(
                pending.EndReasonCode,
                pending.PriorFailureReason,
                pending.PriorException
            );
        }

        if (now < pending.DeadlineRealtimeSeconds)
            return SavedReplayMenuReturnDecision.Wait();

        _pendingMenuReturn = null;
        return SavedReplayMenuReturnDecision.CompleteTimeout(
            pending.EndReasonCode,
            new TimeoutException("Replay menu return was not confirmed before the deadline.")
        );
    }

    // --- Exit-suppression latch (single owner for bootstrapped-exit / native-exit) ---

    public bool IsExitSuppressed(float now) =>
        _replayExitInProgress && now - _replayExitRequestedAt < ExitSuppressionWindowSeconds;

    public void NoteProgrammaticExitLatched(float now)
    {
        _replayExitInProgress = true;
        _replayExitRequestedAt = now;
    }

    public SavedReplayExitRequestDecision RequestBootstrappedExit(float now, bool inReplayState)
    {
        // A replay exit is already in flight (bootstrapped flags cleared, async menu-return has
        // not left ReplayState yet). Report Suppressed so the Exit() prefix patch suppresses the
        // original body — running it now would dispatch the dead replay's despawn GameSim into
        // the live state machine mid transition. Time-bounded: see ExitSuppressionWindowSeconds.
        if (IsExitSuppressed(now) && inReplayState)
            return SavedReplayExitRequestDecision.Suppressed;

        if (!IsSavedReplayPlaybackActive || !_bootstrappedReplayActive)
            return SavedReplayExitRequestDecision.NotActive;

        _returnToMenuAfterReplay = false;
        _bootstrappedReplayActive = false;
        _progress = Progress.Idle;
        NoteProgrammaticExitLatched(now);
        _forceMenuReturnAfterBootstrappedExit = true;
        return SavedReplayExitRequestDecision.Proceed;
    }

    /// <summary>
    /// Clears the exit-in-progress latch once ReplayState is actually gone. This is the only
    /// reliable signal on the bootstrapped exit path, where the state transition happens via
    /// RunManager.ReturnToMainMenu without a normal ReplayState exit event.
    /// </summary>
    public void ObserveReplayStateGone()
    {
        _replayExitInProgress = false;
    }

    /// <summary>Drop a pending menu-return without completing it (runtime destroy path).</summary>
    public void ClearPendingMenuReturn()
    {
        _pendingMenuReturn = null;
    }

    private void ArmPendingMenuReturn(
        float now,
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayPlaybackReasonCode priorFailureReason,
        Exception? priorException
    )
    {
        _pendingMenuReturn = new PendingMenuReturn(
            endReasonCode,
            priorFailureReason,
            priorException,
            now + ExitSuppressionWindowSeconds
        );
    }

    private sealed class PendingMenuReturn
    {
        public PendingMenuReturn(
            ReplayPlaybackEndReasonCode endReasonCode,
            ReplayPlaybackReasonCode priorFailureReason,
            Exception? priorException,
            float deadlineRealtimeSeconds
        )
        {
            EndReasonCode = endReasonCode;
            PriorFailureReason = priorFailureReason;
            PriorException = priorException;
            DeadlineRealtimeSeconds = deadlineRealtimeSeconds;
        }

        public ReplayPlaybackEndReasonCode EndReasonCode { get; }
        public ReplayPlaybackReasonCode PriorFailureReason { get; }
        public Exception? PriorException { get; }
        public float DeadlineRealtimeSeconds { get; }
    }
}

public enum SavedReplayStartTransition
{
    BecameActive,
    BecameIdle,
    Unchanged,
}

public readonly record struct SavedReplayExitOwnership(bool OwnsTerminalByStart);

public readonly record struct SavedReplayStartupInterruption(
    ReplayPlaybackReasonCode ReasonCode,
    Exception? Exception
);

public enum SavedReplayStateExitKind
{
    /// <summary>No terminal action for the runtime (startup owns the terminal, or menu return already pending).</summary>
    Defer,
    CompleteNow,
    BeginMenuReturn,
}

public readonly record struct SavedReplayStateExitDecision(
    SavedReplayStateExitKind Kind,
    ReplayPlaybackReasonCode? LatchReason,
    Exception? LatchException,
    ReplayPlaybackEndReasonCode EndReasonCode,
    ReplayPlaybackReasonCode FailureReason,
    Exception? Exception
)
{
    public static SavedReplayStateExitDecision Defer(
        ReplayPlaybackReasonCode latchReason,
        Exception? latchException
    ) =>
        new(
            SavedReplayStateExitKind.Defer,
            latchReason,
            latchException,
            ReplayPlaybackEndReasonCode.StateExit,
            ReplayPlaybackReasonCode.None,
            null
        );

    public static SavedReplayStateExitDecision Idle() =>
        new(
            SavedReplayStateExitKind.Defer,
            null,
            null,
            ReplayPlaybackEndReasonCode.StateExit,
            ReplayPlaybackReasonCode.None,
            null
        );

    public static SavedReplayStateExitDecision CompleteNow(
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayPlaybackReasonCode failureReason,
        Exception? exception
    ) =>
        new(
            SavedReplayStateExitKind.CompleteNow,
            null,
            null,
            endReasonCode,
            failureReason,
            exception
        );

    public static SavedReplayStateExitDecision BeginMenuReturn(
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayPlaybackReasonCode failureReason,
        Exception? exception
    ) =>
        new(
            SavedReplayStateExitKind.BeginMenuReturn,
            null,
            null,
            endReasonCode,
            failureReason,
            exception
        );
}

public enum SavedReplayMenuReturnKind
{
    None,
    Wait,
    CompleteConfirmed,
    CompleteTimeout,
}

public readonly record struct SavedReplayMenuReturnDecision(
    SavedReplayMenuReturnKind Kind,
    ReplayPlaybackEndReasonCode EndReasonCode,
    ReplayPlaybackReasonCode FailureReason,
    Exception? Exception
)
{
    public static SavedReplayMenuReturnDecision None() =>
        new(
            SavedReplayMenuReturnKind.None,
            ReplayPlaybackEndReasonCode.StateExit,
            ReplayPlaybackReasonCode.None,
            null
        );

    public static SavedReplayMenuReturnDecision Wait() =>
        new(
            SavedReplayMenuReturnKind.Wait,
            ReplayPlaybackEndReasonCode.StateExit,
            ReplayPlaybackReasonCode.None,
            null
        );

    public static SavedReplayMenuReturnDecision CompleteConfirmed(
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayPlaybackReasonCode failureReason,
        Exception? exception
    ) => new(SavedReplayMenuReturnKind.CompleteConfirmed, endReasonCode, failureReason, exception);

    public static SavedReplayMenuReturnDecision CompleteTimeout(
        ReplayPlaybackEndReasonCode endReasonCode,
        Exception exception
    ) =>
        new(
            SavedReplayMenuReturnKind.CompleteTimeout,
            endReasonCode,
            ReplayPlaybackReasonCode.MenuReturnFailed,
            exception
        );
}

public enum SavedReplayExitRequestDecision
{
    Suppressed,
    NotActive,
    Proceed,
}
