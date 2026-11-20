#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay;

internal sealed class SavedReplayLifecycle
{
    /// <summary>ReturnToMainMenu can silently fail after awaiting the network. Let a fresh native Exit through after this window so Continue can recover.</summary>
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

    public bool HasPendingMenuReturn => _pendingMenuReturn != null;

    public bool IsReplayStartInProgress =>
        _progress is Progress.StartInProgress or Progress.StartFailureCleanup;

    // The three start commits keep a mid-start exit from being mistaken for normal playback completion.
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

    public void OnInjectionCommitted()
    {
        _bootstrappedReplayActive = _returnToMenuAfterReplay;
    }

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

    // An interrupted start still owns the terminal event. The ended publish determines which failure it reports.
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

    /// <summary>Clear the pending window so a later tick cannot time out an operation already terminated by dispatch failure.</summary>
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

    public bool IsExitLatched => _replayExitInProgress;

    public bool IsExitSuppressed(float now) =>
        _replayExitInProgress && now - _replayExitRequestedAt < ExitSuppressionWindowSeconds;

    public void NoteProgrammaticExitLatched(float now)
    {
        _replayExitInProgress = true;
        _replayExitRequestedAt = now;
    }

    public SavedReplayExitRequestDecision RequestBootstrappedExit(float now, bool inReplayState)
    {
        // A second native Exit could dispatch the old replay's despawn into the live state machine while menu return is in flight.
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

    /// <summary>ReturnToMainMenu on the bootstrapped path provides no ReplayState exit event. Wait until the Host observes that the state is gone.</summary>
    public bool ObserveReplayStateGone()
    {
        var wasLatched = _replayExitInProgress;
        _replayExitInProgress = false;
        return wasLatched;
    }

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

internal enum SavedReplayStartTransition
{
    BecameActive,
    BecameIdle,
    Unchanged,
}

internal readonly record struct SavedReplayExitOwnership(bool OwnsTerminalByStart);

internal readonly record struct SavedReplayStartupInterruption(
    ReplayPlaybackReasonCode ReasonCode,
    Exception? Exception
);

internal enum SavedReplayStateExitKind
{
    Defer,
    CompleteNow,
    BeginMenuReturn,
}

internal readonly record struct SavedReplayStateExitDecision(
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

internal enum SavedReplayMenuReturnKind
{
    None,
    Wait,
    CompleteConfirmed,
    CompleteTimeout,
}

internal readonly record struct SavedReplayMenuReturnDecision(
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

internal enum SavedReplayExitRequestDecision
{
    Suppressed,
    NotActive,
    Proceed,
}
