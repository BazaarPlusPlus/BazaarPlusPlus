#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.Paths;

namespace BazaarPlusPlus.Game.CombatReplay;

/// <summary>The operation a saved replay session is running, read live.</summary>
public sealed record ActiveSavedReplay(
    string BattleId,
    CombatReplayPlaybackSource Source,
    bool RecordVideo
);

/// <summary>
/// Saved Replay Playback (ADR-0020): one saved or loaded battle's replay on either host, from
/// admission to its terminal event. It owns the payload gate's playback lease, the operation and
/// its one terminal write, the start and its failure fold, the state exit, the bootstrapped
/// <c>ReplayState.Exit</c>, the menu return and teardown, and feeds <see cref="SavedReplayLifecycle"/>
/// its observations in the order that core requires. The game side is the host's
/// <see cref="ISavedReplayHost"/> and the per-start <see cref="ISavedReplayStart"/>. It never exits
/// <c>ReplayState</c> itself (ADR-0002), and every entry point runs on the host's main thread inside
/// its context; the start's awaits resume on the context its caller installed.
/// </summary>
public sealed class SavedReplayPlayback : ISavedReplayRuntime, IDisposable
{
    private const string RuntimeUnavailableReason = "Combat replay runtime is unavailable.";

    private readonly IBppServices _services;
    private readonly RunLifecycleModule _runLifecycle;
    private readonly ReplayPlaybackPublisher _publisher;
    private readonly ReplayPayloadOperationGate _gate;
    private readonly SavedReplayCatalog _catalog;
    private readonly ISavedReplayHost _host;
    private readonly SavedReplayLifecycle _lifecycle = new();
    private ActiveOperation? _active;
    private ReplayPlaybackLogOperation? _completedAwaitingExit;
    private ReplayPlaybackLogOperation? _pendingMenuReturn;
    private bool _disposed;

    public SavedReplayPlayback(
        IBppServices services,
        RunLifecycleModule runLifecycle,
        ReplayPlaybackPublisher publisher,
        ReplayPayloadOperationGate gate,
        SavedReplayCatalog catalog,
        ISavedReplayHost host
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _runLifecycle = runLifecycle ?? throw new ArgumentNullException(nameof(runLifecycle));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public bool IsSavedReplayPlaybackActive => _lifecycle.IsSavedReplayPlaybackActive;

    public bool IsReplayStartInProgress => _lifecycle.IsReplayStartInProgress;

    // A replay is starting, playing, or returning to the menu: History's gate.
    public bool HasSavedReplaySession =>
        _active != null
        || _pendingMenuReturn != null
        || IsReplayStartInProgress
        || IsSavedReplayPlaybackActive;

    /// <summary>The active operation holds the payload gate's playback lease.</summary>
    public bool HoldsPayloadLease => _active?.Lease != null;

    public ActiveSavedReplay? Active =>
        _active is { } active
            ? new ActiveSavedReplay(
                active.Operation.BattleId,
                active.Source,
                active.Operation.RecordVideo
            )
            : null;

    public bool CanReplaySavedCombats(out string reason)
    {
        if (_disposed)
        {
            reason = RuntimeUnavailableReason;
            return false;
        }
        if (IsReplayStartInProgress)
        {
            reason = "A saved replay is already starting.";
            return false;
        }
        if (_services.RunContext.IsInGameRun)
        {
            reason =
                "Saved replay playback is only available while you are outside an active gameplay session.";
            return false;
        }
        if (_host.InReplayState)
        {
            reason = "A replay is already in progress.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public bool CanReplaySavedBattle(string battleId, out string reason)
    {
        if (string.IsNullOrWhiteSpace(battleId))
        {
            reason = "Select a saved battle to replay.";
            return false;
        }
        if (!CanReplaySavedCombats(out reason))
            return false;
        if (!_host.CanOfferSavedReplay(battleId))
        {
            reason = "Replay payload for the selected battle is unavailable.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public bool ReplaySaved(string battleId, bool recordVideo)
    {
        using var scope = _host.EnterHostContext();
        _host.ForgetOffer(battleId ?? string.Empty);
        // Before the gate: a disposed gate throws instead of answering.
        if (_disposed)
        {
            LogRequestRejected(
                CombatReplayPlaybackSource.LocalSaved,
                ReplayRequestRejectionReasonCode.RuntimeUnavailable,
                battleId
            );
            return false;
        }
        var lease = _gate.TryAcquirePlayback();
        if (lease == null)
        {
            LogRequestRejected(
                CombatReplayPlaybackSource.LocalSaved,
                ReplayRequestRejectionReasonCode.PayloadOperationBusy,
                battleId
            );
            return false;
        }
        try
        {
            // The catalog is read uncached: an offer can be stale by the time it is taken.
            if (
                !CanReplaySavedCombats(out _)
                || string.IsNullOrWhiteSpace(battleId)
                || !_catalog.HasSavedReplay(battleId)
            )
            {
                LogRequestRejected(
                    CombatReplayPlaybackSource.LocalSaved,
                    ResolveSavedReplayRejectionReason(battleId),
                    battleId
                );
                return false;
            }
            var manifest = _catalog.LoadBattle(battleId);
            if (manifest == null)
            {
                LogRequestRejected(
                    CombatReplayPlaybackSource.LocalSaved,
                    ReplayRequestRejectionReasonCode.ManifestUnavailable,
                    battleId
                );
                return false;
            }
            var payload = _catalog.LoadPayload(manifest);
            if (payload == null)
            {
                LogRequestRejected(
                    CombatReplayPlaybackSource.LocalSaved,
                    ReplayRequestRejectionReasonCode.PayloadUnavailable,
                    battleId
                );
                return false;
            }
            var heldLease = lease;
            lease = null;
            return Begin(
                manifest,
                payload,
                battleId,
                CombatReplayPlaybackSource.LocalSaved,
                recordVideo,
                heldLease
            );
        }
        finally
        {
            lease?.Dispose();
        }
    }

    // Reads a saved battle under a transient playback lease, so the sweep cannot evict it
    // mid-read. Busy while maintenance, persistence or a playback holds the gate.
    public SavedBattleLoadOutcome TryLoadSavedBattle(
        string battleId,
        out PvpBattleManifest? manifest,
        out PvpReplayPayload? payload
    )
    {
        manifest = null;
        payload = null;
        if (_disposed)
            return SavedBattleLoadOutcome.Unavailable;
        using var lease = _gate.TryAcquirePlayback();
        if (lease == null)
            return SavedBattleLoadOutcome.Busy;
        return _catalog.TryLoad(battleId, out manifest, out payload);
    }

    // Plays a manifest and payload the caller already holds, so it takes no playback lease.
    public bool ReplayLoadedBattle(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        bool recordVideo,
        CombatReplayPlaybackSource source
    )
    {
        if (source == CombatReplayPlaybackSource.CurrentNative)
            throw new ArgumentOutOfRangeException(nameof(source), source, null);
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        using var scope = _host.EnterHostContext();
        if (!CanReplaySavedCombats(out _))
        {
            LogRequestRejected(source, ResolveGeneralReplayRejectionReason(), manifest.BattleId);
            return false;
        }
        return Begin(manifest, payload, manifest.BattleId, source, recordVideo, lease: null);
    }

    /// <summary>Every <c>ReplayState</c> exit, whether or not a saved replay is running.</summary>
    public void OnReplayStateExited()
    {
        if (_disposed)
            return;
        using var scope = _host.EnterHostContext();
        var now = _host.Now;
        var ownership = _lifecycle.BeginReplayStateExit(now);
        var operation = _active?.Operation ?? _completedAwaitingExit;
        _completedAwaitingExit = null;
        // The ended publish clears any publisher session, a current replay's included.
        var ended = ReplayPlaybackCleanup.PublishThenCleanup(
            () => _publisher.PublishEnded("state-exit", failed: ownership.OwnsTerminalByStart),
            (stage, exception) => LogCleanupFailure(stage, operation?.BattleId, exception),
            _host.CleanupSteps(SavedReplayCleanupPath.StateExit, operation?.BattleId)
        );
        var decision = _lifecycle.OnReplayStateExited(now, ended.Succeeded, ended.Exception);
        if (decision.Kind == SavedReplayStateExitKind.Defer || operation == null)
            return;
        if (decision.Kind == SavedReplayStateExitKind.CompleteNow)
        {
            Complete(
                operation,
                decision.EndReasonCode,
                ReplayRollbackStatus.NotRequired,
                decision.FailureReason,
                decision.Exception
            );
            return;
        }
        BeginPendingMenuReturn(operation, decision.EndReasonCode);
    }

    /// <summary>
    /// The bootstrapped <c>ReplayState.Exit</c> prefix: true when the exit is handled here and the
    /// game's own body must be skipped, false to let the game exit as usual.
    /// </summary>
    public bool TryExitBootstrapped()
    {
        if (_disposed)
            return false;
        using var scope = _host.EnterHostContext();
        var now = _host.Now;
        var decision = _lifecycle.RequestBootstrappedExit(now, _host.InReplayState);
        var outcome = decision switch
        {
            SavedReplayExitRequestDecision.Suppressed => SavedReplayExitOutcome.Suppressed,
            SavedReplayExitRequestDecision.Proceed => SavedReplayExitOutcome.Proceed,
            _ => SavedReplayExitOutcome.NotActive,
        };
        _host.Notify(
            new SavedReplayNotice(
                SavedReplayNoticeKind.ExitRequested,
                _active?.Operation.BattleId,
                ExitOutcome: outcome
            )
        );
        switch (outcome)
        {
            // Suppressed: report handled so the prefix skips the original body.
            case SavedReplayExitOutcome.Suppressed:
                return true;
            case SavedReplayExitOutcome.NotActive:
                return false;
        }

        // The Exit prefix skips the game's own transition, so the ended signal is published here
        // as the state-exit path does; otherwise a recorder never sees the replay end.
        var operation = _active?.Operation ?? _completedAwaitingExit;
        _completedAwaitingExit = null;
        var ended = ReplayPlaybackCleanup.PublishThenCleanup(
            () => _publisher.PublishEnded("saved-replay-exit", failed: false),
            (stage, exception) => LogCleanupFailure(stage, operation?.BattleId, exception),
            _host.CleanupSteps(SavedReplayCleanupPath.BootstrappedExit, operation?.BattleId)
        );
        var exitDecision = _lifecycle.OnReplayStateExited(now, ended.Succeeded, ended.Exception);
        if (operation != null && exitDecision.Kind == SavedReplayStateExitKind.BeginMenuReturn)
            BeginPendingMenuReturn(operation, exitDecision.EndReasonCode);
        return true;
    }

    /// <summary>
    /// Every frame: confirms a pending menu return and clears the exit latch once
    /// <c>ReplayState</c> is gone. Nothing exits on a tick.
    /// </summary>
    public void Tick()
    {
        if (_disposed)
            return;
        using var scope = _host.EnterHostContext();
        var lobbyLoaded = _lifecycle.HasPendingMenuReturn && _host.LobbyLoaded;
        var decision = _lifecycle.TickMenuReturn(_host.Now, lobbyLoaded);
        if (
            decision.Kind
            is SavedReplayMenuReturnKind.CompleteConfirmed
                or SavedReplayMenuReturnKind.CompleteTimeout
        )
        {
            var operation = _pendingMenuReturn;
            _pendingMenuReturn = null;
            if (operation != null)
            {
                _host.Notify(
                    new SavedReplayNotice(
                        SavedReplayNoticeKind.MenuReturnSettled,
                        operation.BattleId,
                        TimedOut: decision.Kind == SavedReplayMenuReturnKind.CompleteTimeout
                    )
                );
                Complete(
                    operation,
                    decision.EndReasonCode,
                    ReplayRollbackStatus.NotRequired,
                    decision.FailureReason,
                    decision.Exception
                );
            }
        }
        // The only reliable signal on the bootstrapped exit path, where the state leaves through
        // the menu return without a ReplayState exit event.
        if (!_host.InReplayState)
            _lifecycle.ObserveReplayStateGone();
    }

    /// <summary>
    /// The desktop's managed recording restart promotes the playing operation to recording.
    /// </summary>
    public bool TryPromoteActiveToRecording() => _active?.Operation.TryPromoteToRecording() == true;

    /// <summary>
    /// A failed managed recording restart: publishes the ended signal, completes the playing
    /// operation as a failed start, frees its lease, and parks it for the next state exit.
    /// Returns the failure the operation was completed with.
    /// </summary>
    public Exception? FailActiveRecordingRestart(
        string endReason,
        ReplayPlaybackReasonCode reasonCode,
        Exception? exception
    )
    {
        using var scope = _host.EnterHostContext();
        var operation = _active?.Operation;
        if (operation == null)
            return exception;
        var ended = _publisher.PublishEnded(endReason, failed: true);
        var failureReason = ended.Succeeded
            ? reasonCode
            : ReplayPlaybackReasonCode.EndedPublishFailed;
        var failureException = ended.Succeeded ? exception : ended.Exception;
        Complete(
            operation,
            ReplayPlaybackEndReasonCode.StartFailed,
            ReplayRollbackStatus.NotRequired,
            failureReason,
            failureException
        );
        _completedAwaitingExit = operation;
        return failureException;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        using var scope = _host.EnterHostContext();
        var active = _active;
        if (active != null)
        {
            var ended = _publisher.PublishEnded("runtime-destroyed", failed: true);
            Complete(
                active.Operation,
                ReplayPlaybackEndReasonCode.RuntimeDestroyed,
                ReplayRollbackStatus.NotRequired,
                ended.Succeeded
                    ? ReplayPlaybackReasonCode.StartException
                    : ReplayPlaybackReasonCode.EndedPublishFailed,
                ended.Exception
            );
        }
        _active = null;
        active?.Lease?.Dispose();
        _completedAwaitingExit = null;
        _pendingMenuReturn = null;
        _lifecycle.ClearPendingMenuReturn();
    }

    private bool Begin(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        string battleId,
        CombatReplayPlaybackSource source,
        bool recordVideo,
        IDisposable? lease
    )
    {
        var operation = new ReplayPlaybackLogOperation(
            battleId,
            source,
            recordVideo && _host.RecordsVideo
        );
        // The displaced operation is not completed, but its lease goes with it: only the active
        // operation ever holds one.
        var displaced = _active;
        _completedAwaitingExit = null;
        _active = new ActiveOperation(operation, source, lease);
        displaced?.Lease?.Dispose();
        _host.Notify(new SavedReplayNotice(SavedReplayNoticeKind.Accepted, battleId, source));

        ISavedReplayStart start;
        try
        {
            start = _host.BeginStart(manifest, payload, operation);
        }
        catch (Exception exception)
        {
            Complete(
                operation,
                ReplayPlaybackEndReasonCode.StartFailed,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.StartException,
                exception
            );
            return false;
        }
        _lifecycle.OnStartBegun();
        _host.Notify(new SavedReplayNotice(SavedReplayNoticeKind.StartBegun, battleId, source));
        // Started inside the caller's host context, so its awaits resume there; it takes no scope
        // of its own across an await.
        _ = StartAsync(manifest, source, operation, start);
        return true;
    }

    private async Task StartAsync(
        PvpBattleManifest manifest,
        CombatReplayPlaybackSource source,
        ReplayPlaybackLogOperation operation,
        ISavedReplayStart start
    )
    {
        var battleId = operation.BattleId;
        var attemptedBootstrap = false;
        _publisher.BeginSession(battleId, manifest, source, operation.RecordVideo);
        try
        {
            start.Prepare();
            _runLifecycle.RefreshRunStateFromCurrentState();
            attemptedBootstrap = start.NeedsBootstrap;
            var bootstrappedFromLobby = await start.BootstrapAsync();
            _lifecycle.OnBootstrapResolved(bootstrappedFromLobby);
            try
            {
                await start.PrepareOpponentAsync();
            }
            catch (Exception exception)
            {
                operation.ReportDegradation(
                    ReplayPlaybackReasonCode.OpponentPortraitUnavailable,
                    exception
                );
            }
            ReplayRunEconomyFallback.ApplyMissingRunEconomy(
                manifest,
                PathConstants.RunLogDatabase(_services.Paths.RequireDataRoot()),
                operation
            );
            await start.InjectAsync(_publisher.PublishStarting);
            var interruption = _lifecycle.TakeStartupInterruption();
            if (interruption != null)
                throw new ReplayPlaybackStartException(
                    interruption.Value.ReasonCode,
                    interruption.Value.Exception
                );
            _lifecycle.OnInjectionCommitted();
            operation.MarkStarted();
        }
        catch (Exception exception)
        {
            _lifecycle.OnStartFailed();
            _host.Notify(
                new SavedReplayNotice(SavedReplayNoticeKind.StartFailed, battleId, source)
            );
            // Unconditional: the ended publish always clears the session of a failed start.
            var ended = ReplayPlaybackCleanup.PublishThenCleanup(
                () => _publisher.PublishEnded("start-failed", failed: true),
                (stage, cleanupException) => LogCleanupFailure(stage, battleId, cleanupException),
                _host.CleanupSteps(SavedReplayCleanupPath.StartFailed, battleId)
            );
            var (failureReason, failureException) = exception is ReplayPlaybackStartException known
                ? (known.ReasonCode, known.InnerException)
                : (ReplayPlaybackReasonCode.StartException, exception.InnerException ?? exception);
            if (!ended.Succeeded)
            {
                failureReason = ReplayPlaybackReasonCode.EndedPublishFailed;
                failureException = ended.Exception;
            }
            var rollbackStatus = ReplayRollbackStatus.NotRequired;
            if (attemptedBootstrap)
            {
                var rollback = await RollbackAsync(start);
                rollbackStatus = rollback.Succeeded
                    ? ReplayRollbackStatus.Succeeded
                    : ReplayRollbackStatus.Failed;
                if (!rollback.Succeeded)
                {
                    failureReason = ReplayPlaybackReasonCode.BootstrapRollbackFailed;
                    failureException = rollback.Exception;
                }
            }
            Complete(
                operation,
                ReplayPlaybackEndReasonCode.StartFailed,
                rollbackStatus,
                failureReason,
                failureException
            );
        }
        finally
        {
            _ = _lifecycle.OnStartFinished();
        }
    }

    private static async Task<ReplayBootstrapRollback> RollbackAsync(ISavedReplayStart start)
    {
        try
        {
            return await start.RollbackBootstrapAsync();
        }
        catch (Exception exception)
        {
            return ReplayBootstrapRollback.Failure(exception);
        }
    }

    private void BeginPendingMenuReturn(
        ReplayPlaybackLogOperation operation,
        ReplayPlaybackEndReasonCode endReasonCode
    )
    {
        // The lifecycle armed the pending window when it decided BeginMenuReturn; a failed
        // dispatch clears it so the tick cannot later time out an operation completed here.
        try
        {
            _host.ReturnToMainMenu();
        }
        catch (Exception exception)
        {
            _lifecycle.OnMenuReturnDispatchFailed();
            _pendingMenuReturn = null;
            Complete(
                operation,
                endReasonCode,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.MenuReturnFailed,
                exception
            );
            return;
        }
        _pendingMenuReturn = operation;
        _host.Notify(
            new SavedReplayNotice(
                SavedReplayNoticeKind.MenuReturnStarted,
                operation.BattleId,
                EndReason: endReasonCode
            )
        );
    }

    // The operation writes its terminal event once; only the active one holds a lease.
    private void Complete(
        ReplayPlaybackLogOperation operation,
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayRollbackStatus rollbackStatus,
        ReplayPlaybackReasonCode failureReasonCode,
        Exception? exception
    )
    {
        operation.Complete(endReasonCode, rollbackStatus, failureReasonCode, exception);
        var active = _active;
        if (active == null || !ReferenceEquals(active.Operation, operation))
            return;
        _active = null;
        active.Lease?.Dispose();
        _host.Notify(new SavedReplayNotice(SavedReplayNoticeKind.Completed, operation.BattleId));
    }

    private ReplayRequestRejectionReasonCode ResolveSavedReplayRejectionReason(string? battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return ReplayRequestRejectionReasonCode.InvalidBattleId;
        var general = ResolveGeneralReplayRejectionReason();
        if (general != ReplayRequestRejectionReasonCode.RuntimeUnavailable)
            return general;
        return !_catalog.HasSavedReplay(battleId)
            ? ReplayRequestRejectionReasonCode.PayloadUnavailable
            : ReplayRequestRejectionReasonCode.RuntimeUnavailable;
    }

    private ReplayRequestRejectionReasonCode ResolveGeneralReplayRejectionReason()
    {
        if (_disposed)
            return ReplayRequestRejectionReasonCode.RuntimeUnavailable;
        if (IsReplayStartInProgress)
            return ReplayRequestRejectionReasonCode.ReplayAlreadyStarting;
        if (_services.RunContext.IsInGameRun)
            return ReplayRequestRejectionReasonCode.ActiveRun;
        if (_host.InReplayState)
            return ReplayRequestRejectionReasonCode.ReplayAlreadyActive;
        return ReplayRequestRejectionReasonCode.RuntimeUnavailable;
    }

    private static void LogRequestRejected(
        CombatReplayPlaybackSource source,
        ReplayRequestRejectionReasonCode reasonCode,
        string? battleId
    ) =>
        BppLog.InfoEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.playback.request_rejected"
            ),
            ("source", source),
            ("reason_code", reasonCode),
            ("battle_id", battleId, BppLogCorrelationPolicy.Short)
        );

    private static void LogCleanupFailure(string stage, string? battleId, Exception exception) =>
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.CombatReplay,
                "combat_replay.playback.cleanup_observed"
            ),
            exception,
            () =>
                [
                    ("stage", stage),
                    ("removed_count", 0),
                    ("battle_id", battleId, BppLogCorrelationPolicy.Short),
                ]
        );

    private sealed record ActiveOperation(
        ReplayPlaybackLogOperation Operation,
        CombatReplayPlaybackSource Source,
        IDisposable? Lease
    );
}
