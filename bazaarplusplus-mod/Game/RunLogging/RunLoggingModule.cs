#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLogging.Models;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLoggingModule
{
    private static readonly TimeSpan ReplayPersistenceCompletionGracePeriod = TimeSpan.FromSeconds(
        2
    );

    private readonly IBppEventBus _eventBus;
    private readonly RunLogSessionManager _sessionManager;
    private readonly RunLoggingControllerCore _core;
    private readonly Func<bool> _hasPendingReplayPersistence;
    private readonly Func<RunLogSessionState?> _ensureActiveRunFromGame;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _pvpBattleSubscription;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _replayPersistenceDrainedSubscription;
    private RunLogCompletion? _deferredRunCompletion;
    private string? _deferredRunCompletionRunId;
    private DateTime? _deferredRunCompletionDeadlineUtc;
    private string? _pendingInterruptedRunId;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<string, RunLogCompletion> _buildRunLogCompletion;
    private readonly Func<string, RunLogAbandonment> _buildRunLogAbandonment;

    public RunLoggingModule(
        IBppEventBus eventBus,
        RunLogSessionManager sessionManager,
        RunLoggingControllerCore core,
        Func<bool> hasPendingReplayPersistence,
        Func<RunLogSessionState?> ensureActiveRunFromGame,
        Func<DateTime>? utcNow = null,
        Func<string, RunLogCompletion>? buildRunLogCompletion = null,
        Func<string, RunLogAbandonment>? buildRunLogAbandonment = null
    )
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _hasPendingReplayPersistence =
            hasPendingReplayPersistence
            ?? throw new ArgumentNullException(nameof(hasPendingReplayPersistence));
        _ensureActiveRunFromGame =
            ensureActiveRunFromGame
            ?? throw new ArgumentNullException(nameof(ensureActiveRunFromGame));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _buildRunLogCompletion =
            buildRunLogCompletion ?? RunLoggingGameDataReader.BuildRunLogCompletion;
        _buildRunLogAbandonment =
            buildRunLogAbandonment ?? RunLoggingGameDataReader.BuildRunLogAbandonment;
    }

    public void Start()
    {
        _runLifecycleSubscription = _eventBus.Subscribe<RunLifecycleChanged>(OnRunLifecycleChanged);
        _pvpBattleSubscription = _eventBus.Subscribe<PvpBattleRecorded>(OnPvpBattleRecorded);
        _runInitializedSubscription = _eventBus.Subscribe<RunInitializedObserved>(
            OnRunInitializedObserved
        );
        _replayPersistenceDrainedSubscription = _eventBus.Subscribe<CombatReplayPersistenceDrained>(
            OnCombatReplayPersistenceDrained
        );
    }

    public void Stop()
    {
        if (_deferredRunCompletion != null && _sessionManager.HasActiveSession)
        {
            try
            {
                TryCompleteDeferredRunExit(forceCompletion: true);
            }
            catch (Exception ex)
            {
                BppLog.Error(
                    "RunLoggingModule",
                    $"Failed to finalize deferred run completion during teardown: {ex}"
                );
            }
        }

        ClearDeferredRunCompletion();
        ClearPendingInterruptedRun();
        _replayPersistenceDrainedSubscription?.Dispose();
        _replayPersistenceDrainedSubscription = null;
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
        _pvpBattleSubscription?.Dispose();
        _pvpBattleSubscription = null;
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        try
        {
            if (!BppRuntimeHost.RunContext.IsInGameRun)
                return;

            HandleRunActivation(observed.RunId);
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingModule", $"Run initialization handling failed: {ex}");
        }
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        try
        {
            if (change.IsInGameRun)
            {
                HandleRunActivation(BppRuntimeHost.RunContext.CurrentServerRunId);
                return;
            }

            if (!_sessionManager.HasActiveSession)
                return;

            var activeSession = _sessionManager.ActiveSession;
            if (activeSession == null)
                return;

            if (IsInterruptedTransition(change))
            {
                _pendingInterruptedRunId = activeSession.RunId;
                return;
            }

            if (!IsCompletedTransition(change))
                return;

            ClearPendingInterruptedRun();
            _deferredRunCompletionRunId = activeSession.RunId;
            _deferredRunCompletion = _buildRunLogCompletion("run_state_exit");
            TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingModule", $"Run lifecycle transition handling failed: {ex}");
        }
    }

    private void OnPvpBattleRecorded(PvpBattleRecorded recorded)
    {
        try
        {
            var manifest = recorded.Manifest;
            var inRun = BppRuntimeHost.RunContext.IsInGameRun;
            if (
                manifest == null
                || !string.Equals(manifest.CombatKind, "PVPCombat", StringComparison.Ordinal)
            )
            {
                return;
            }

            if (!TryResolveReplayTargetSession(manifest, inRun))
                return;

            _core.AcceptCombatReplay(
                new RunLogPvpBattleInput
                {
                    Day = manifest.Day,
                    Hour = manifest.Hour,
                    EncounterId = manifest.EncounterId,
                    CombatKind = manifest.CombatKind,
                    BattleId = manifest.BattleId,
                    OpponentName = manifest.Participants.OpponentName,
                }
            );

            if (!inRun)
                TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingModule", $"PVP battle capture failed: {ex}");
        }
    }

    private void OnCombatReplayPersistenceDrained(CombatReplayPersistenceDrained drained)
    {
        try
        {
            if (!BppRuntimeHost.RunContext.IsInGameRun)
                TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            BppLog.Error("RunLoggingModule", $"Replay persistence drain handling failed: {ex}");
        }
    }

    private void HandleRunActivation(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return;

        var activeSession = _sessionManager.ActiveSession;
        if (
            !string.IsNullOrWhiteSpace(_pendingInterruptedRunId)
            && activeSession != null
            && string.Equals(
                activeSession.RunId,
                _pendingInterruptedRunId,
                StringComparison.Ordinal
            )
        )
        {
            if (string.Equals(runId, _pendingInterruptedRunId, StringComparison.Ordinal))
            {
                ClearPendingInterruptedRun();
            }
            else
            {
                _sessionManager.MarkRunAbandoned(_buildRunLogAbandonment("run_interrupted"));
                ClearPendingInterruptedRun();
            }
        }

        CancelDeferredRunExitIfRunResumed();
        _ensureActiveRunFromGame();
    }

    private bool TryResolveReplayTargetSession(PvpBattleManifest manifest, bool inRun)
    {
        if (inRun)
        {
            var session = _ensureActiveRunFromGame();
            if (session == null)
                return false;

            if (
                !string.IsNullOrWhiteSpace(manifest.RunId)
                && !string.Equals(session.RunId, manifest.RunId, StringComparison.Ordinal)
            )
            {
                BppLog.Warn(
                    "RunLoggingModule",
                    $"Skipping replay event for run {manifest.RunId} because active in-run session is {session.RunId}."
                );
                return false;
            }

            return true;
        }

        var deferredSession = _sessionManager.ActiveSession;
        if (deferredSession == null)
            return false;

        if (string.IsNullOrWhiteSpace(manifest.RunId))
        {
            BppLog.Warn(
                "RunLoggingModule",
                $"Skipping deferred replay event for battle {manifest.BattleId} because manifest run id is unavailable."
            );
            return false;
        }

        if (!string.Equals(deferredSession.RunId, manifest.RunId, StringComparison.Ordinal))
        {
            BppLog.Warn(
                "RunLoggingModule",
                $"Skipping deferred replay event for run {manifest.RunId} because active deferred session is {deferredSession.RunId}."
            );
            return false;
        }

        return true;
    }

    private bool TryCompleteDeferredRunExit(bool forceCompletion = false)
    {
        if (_deferredRunCompletion == null)
            return false;

        var activeSession = _sessionManager.ActiveSession;
        if (
            activeSession == null
            || !string.Equals(
                activeSession.RunId,
                _deferredRunCompletionRunId,
                StringComparison.Ordinal
            )
        )
        {
            ClearDeferredRunCompletion();
            return false;
        }

        var now = _utcNow();
        if (!forceCompletion && _hasPendingReplayPersistence())
        {
            var deadline =
                _deferredRunCompletionDeadlineUtc ?? (now + ReplayPersistenceCompletionGracePeriod);
            _deferredRunCompletionDeadlineUtc = deadline;
            if (now < deadline)
                return false;

            BppLog.Warn(
                "RunLoggingModule",
                "Completing run before replay persistence drained after grace timeout."
            );
        }
        else if (forceCompletion && _hasPendingReplayPersistence())
        {
            BppLog.Warn(
                "RunLoggingModule",
                "Completing deferred run during teardown before replay persistence drained."
            );
        }

        _core.CompleteRun(_deferredRunCompletion);
        ClearDeferredRunCompletion();
        return true;
    }

    private void CancelDeferredRunExitIfRunResumed()
    {
        if (
            _deferredRunCompletion == null
            || string.IsNullOrWhiteSpace(_deferredRunCompletionRunId)
        )
            return;

        var currentRunId = BppRuntimeHost.RunContext.CurrentServerRunId;
        var activeSession = _sessionManager.ActiveSession;
        if (
            activeSession != null
            && string.Equals(
                activeSession.RunId,
                _deferredRunCompletionRunId,
                StringComparison.Ordinal
            )
            && string.Equals(currentRunId, _deferredRunCompletionRunId, StringComparison.Ordinal)
        )
        {
            ClearDeferredRunCompletion();
        }
    }

    private void ClearDeferredRunCompletion()
    {
        _deferredRunCompletion = null;
        _deferredRunCompletionRunId = null;
        _deferredRunCompletionDeadlineUtc = null;
    }

    private void ClearPendingInterruptedRun()
    {
        _pendingInterruptedRunId = null;
    }

    private static bool IsCompletedTransition(RunLifecycleChanged change)
    {
        return string.Equals(change.Reason, "Run ended", StringComparison.Ordinal);
    }

    private static bool IsInterruptedTransition(RunLifecycleChanged change)
    {
        return string.Equals(change.Reason, "Run interrupted", StringComparison.Ordinal);
    }
}
