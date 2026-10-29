#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Storage.RunLog;

namespace BazaarPlusPlus.Game.RunLogging;

/// <summary>
/// Run Logging: turns run lifecycle, run initialization, recorded PvP battles, and replay drains
/// into runs, run events, and battle-to-run links in the history database. Every host composes it
/// the same way; the host supplies the store, the snapshot probe, and the battle link.
/// </summary>
public sealed class RunLoggingModule : IBppFeature
{
    private static readonly TimeSpan ReplayPersistenceCompletionGracePeriod = TimeSpan.FromSeconds(
        2
    );

    // The game fetches a Ranked Run's new rating while its end-of-run screen loads, after the Run
    // has exited; until then the cached rank still holds the starting rating.
    private static readonly TimeSpan RankRefreshCompletionGracePeriod = TimeSpan.FromSeconds(15);

    private readonly IBppEventBus _eventBus;
    private readonly object _lifecycleGate = new();
    private readonly IRunContext _runContext;
    private readonly IRunSnapshotProbe _snapshotProbe;
    private readonly string _buildChannel;
    private readonly string? _databasePath;
    private readonly Func<IRunLogStore> _storeFactory;
    private readonly Action<string, string> _attachBattleToRun;
    private readonly Func<bool> _hasPendingReplayPersistence;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, Action, IDisposable> _scheduleDeferredCompletion;
    private readonly Func<string?> _playerAccountIdResolver;
    private readonly Func<bool> _bundleScreenshotRequestedResolver;
    private readonly Func<string?> _modVersionResolver;
    private readonly Func<Action<RankSnapshot>, IDisposable>? _subscribeRankRefreshed;
    private IDisposable? _storeLifetime;
    private RunLogSessionManager? _sessionManager;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _pvpBattleSubscription;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _replayPersistenceDrainedSubscription;
    private IDisposable? _rankRefreshedSubscription;
    private RunLogCompletion? _deferredRunCompletion;
    private string? _deferredRunCompletionRunId;
    private bool _deferredRunCompletionAwaitsRank;
    private DateTimeOffset? _deferredRunCompletionDeadlineUtc;
    private IDisposable? _deferredRunCompletionTimer;
    private string? _pendingInterruptedRunId;
    private string? _startedEventRunId;
    private bool _started;
    private bool _stopped;

    /// <summary>
    /// Builds the module; nothing opens until <see cref="Start"/>. <c>attachBattleToRun</c> links a
    /// saved battle (battle id) to a run (run id) in the host's battle store.
    /// <c>subscribeRankRefreshed</c> calls back on the main thread each time the game replaces its
    /// cached rank; a completed Ranked Run waits for that refresh to record its final rating.
    /// </summary>
    public RunLoggingModule(
        IBppEventBus eventBus,
        IRunContext runContext,
        IRunSnapshotProbe snapshotProbe,
        string buildChannel,
        Func<IRunLogStore> storeFactory,
        Action<string, string> attachBattleToRun,
        Func<bool> hasPendingReplayPersistence,
        Func<DateTimeOffset> utcNow,
        string? databasePath = null,
        Func<TimeSpan, Action, IDisposable>? scheduleDeferredCompletion = null,
        Func<string?>? playerAccountIdResolver = null,
        Func<bool>? bundleScreenshotRequestedResolver = null,
        Func<string?>? modVersionResolver = null,
        Func<Action<RankSnapshot>, IDisposable>? subscribeRankRefreshed = null
    )
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        _snapshotProbe = snapshotProbe ?? throw new ArgumentNullException(nameof(snapshotProbe));
        _buildChannel = buildChannel ?? string.Empty;
        _databasePath = databasePath;
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        _attachBattleToRun =
            attachBattleToRun ?? throw new ArgumentNullException(nameof(attachBattleToRun));
        _hasPendingReplayPersistence =
            hasPendingReplayPersistence
            ?? throw new ArgumentNullException(nameof(hasPendingReplayPersistence));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _scheduleDeferredCompletion =
            scheduleDeferredCompletion ?? ScheduleDeferredCompletionWithTimer;
        _playerAccountIdResolver = playerAccountIdResolver ?? (() => null);
        _bundleScreenshotRequestedResolver = bundleScreenshotRequestedResolver ?? (() => false);
        _modVersionResolver = modVersionResolver ?? (() => null);
        _subscribeRankRefreshed = subscribeRankRefreshed;
    }

    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_started || _stopped)
                return;

            try
            {
                InitializeStore();
                _runLifecycleSubscription = _eventBus.Subscribe<RunLifecycleChanged>(
                    OnRunLifecycleChanged
                );
                _pvpBattleSubscription = _eventBus.Subscribe<PvpBattleRecorded>(
                    OnPvpBattleRecorded
                );
                _runInitializedSubscription = _eventBus.Subscribe<RunInitializedObserved>(
                    OnRunInitializedObserved
                );
                _replayPersistenceDrainedSubscription =
                    _eventBus.Subscribe<CombatReplayPersistenceDrained>(
                        OnCombatReplayPersistenceDrained
                    );
                _rankRefreshedSubscription = TrySubscribeRankRefreshed();
                _started = true;
            }
            catch
            {
                DisposeSubscriptions();
                DisposeStore();
                throw;
            }
        }
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.RunLogging, "run_logging.store.ready"),
            () => new BppLogField[] { ("database_path", _databasePath) }
        );
    }

    public void Stop()
    {
        lock (_lifecycleGate)
        {
            if (_stopped)
                return;

            var sessionManager = _sessionManager;
            if (
                _deferredRunCompletion != null
                && sessionManager != null
                && sessionManager.HasActiveSession
            )
            {
                var runId = _deferredRunCompletionRunId ?? sessionManager.ActiveSession?.RunId;
                try
                {
                    TryCompleteDeferredRunExit(forceCompletion: true);
                }
                catch (Exception ex)
                {
                    BppLog.ErrorEvent(
                        new BppLogEvent(
                            BppLogFeatureScope.RunLogging,
                            "run_logging.run.completion_failed",
                            storm: []
                        ),
                        ex,
                        ("run_id", runId, BppLogCorrelationPolicy.Short),
                        ("reason_code", RunLoggingReasonCode.TeardownFinalizationException)
                    );
                }
            }

            ClearDeferredRunCompletion();
            ClearPendingInterruptedRun();
            DisposeSubscriptions();
            _started = false;
            _stopped = true;
            DisposeStore();
        }
    }

    private void InitializeStore()
    {
        var store =
            _storeFactory()
            ?? throw new InvalidOperationException("Run logging store factory returned null.");
        _storeLifetime = store as IDisposable;
        _sessionManager = new RunLogSessionManager(
            store,
            _utcNow,
            statsProvider: () => _snapshotProbe.TryGetPlayerStats(out var stats) ? stats : null
        );
        _sessionManager.RestoreActiveSession();
    }

    private void DisposeStore()
    {
        var lifetime = _storeLifetime;
        _storeLifetime = null;
        _sessionManager = null;
        lifetime?.Dispose();
    }

    private void DisposeSubscriptions()
    {
        _rankRefreshedSubscription?.Dispose();
        _rankRefreshedSubscription = null;
        _replayPersistenceDrainedSubscription?.Dispose();
        _replayPersistenceDrainedSubscription = null;
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
        _pvpBattleSubscription?.Dispose();
        _pvpBattleSubscription = null;
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;
    }

    // Without the subscription a Ranked Run completes at once with the rating cached at exit.
    private IDisposable? TrySubscribeRankRefreshed()
    {
        if (_subscribeRankRefreshed == null)
            return null;
        try
        {
            return _subscribeRankRefreshed(OnRankRefreshed);
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.rank_refresh.subscribe_failed",
                    storm: []
                ),
                ex,
                ("reason_code", RunLoggingReasonCode.RankRefreshSubscribeException)
            );
            return null;
        }
    }

    private static IDisposable ScheduleDeferredCompletionWithTimer(
        TimeSpan delay,
        Action callback
    ) => new Timer(_ => callback(), null, delay, Timeout.InfiniteTimeSpan);

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        lock (_lifecycleGate)
        {
            if (!_started || _stopped)
                return;
            OnRunInitializedObservedUnderLock(observed);
        }
    }

    private void OnRunInitializedObservedUnderLock(RunInitializedObserved observed)
    {
        try
        {
            if (!_runContext.IsInGameRun)
                return;

            HandleRunActivation(observed.RunId);
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.run.activation_failed",
                    storm: []
                ),
                ex,
                ("run_id", observed.RunId, BppLogCorrelationPolicy.Short),
                ("reason_code", RunLoggingReasonCode.RunActivationException)
            );
        }
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        lock (_lifecycleGate)
        {
            if (!_started || _stopped)
                return;
            OnRunLifecycleChangedUnderLock(change);
        }
    }

    private void OnRunLifecycleChangedUnderLock(RunLifecycleChanged change)
    {
        string? runId = null;
        var transition = RunLoggingTransition.Unknown;
        try
        {
            var sessionManager = RequireSessionManager();
            runId = sessionManager.ActiveSession?.RunId ?? _runContext.CurrentServerRunId;
            transition = ToLogTransition(change);
            if (change.IsInGameRun)
            {
                HandleRunActivation(_runContext.CurrentServerRunId);
                return;
            }

            if (!sessionManager.HasActiveSession)
                return;

            var activeSession = sessionManager.ActiveSession;
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
            ClearDeferredRunCompletion();
            _deferredRunCompletionRunId = activeSession.RunId;
            _deferredRunCompletion = BuildRunLogCompletion("run_state_exit");
            _deferredRunCompletionAwaitsRank =
                _rankRefreshedSubscription != null
                && string.Equals(
                    ReadRunBasics()?.GameMode,
                    "Ranked",
                    StringComparison.OrdinalIgnoreCase
                );
            TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.run.transition_failed",
                    storm: []
                ),
                ex,
                ("run_id", runId, BppLogCorrelationPolicy.Short),
                ("transition", transition),
                ("reason_code", RunLoggingReasonCode.RunTransitionException)
            );
        }
    }

    private void OnPvpBattleRecorded(PvpBattleRecorded recorded)
    {
        lock (_lifecycleGate)
        {
            if (!_started || _stopped)
                return;
            OnPvpBattleRecordedUnderLock(recorded);
        }
    }

    private void OnPvpBattleRecordedUnderLock(PvpBattleRecorded recorded)
    {
        try
        {
            var inRun = _runContext.IsInGameRun;
            if (!string.Equals(recorded.CombatKind, "PVPCombat", StringComparison.Ordinal))
                return;

            var session = TryResolveReplayTargetSession(recorded, inRun);
            if (session == null)
                return;

            if (!string.IsNullOrWhiteSpace(recorded.BattleId))
                _attachBattleToRun(recorded.BattleId, session.RunId);

            var sessionManager = RequireSessionManager();
            sessionManager.AppendEvent(
                new RunLogEvent
                {
                    Kind = "pvp_combat_recorded",
                    Day = recorded.Day,
                    Hour = recorded.Hour,
                    EncounterId = recorded.EncounterId,
                    CombatKind = recorded.CombatKind,
                    BattleId = recorded.BattleId,
                    OpponentName = recorded.OpponentName,
                }
            );
            sessionManager.SaveCheckpoint();

            if (!inRun)
                TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            EmitBattleCaptureFailure(recorded, RunLoggingReasonCode.BattleCaptureException, ex);
        }
    }

    private void OnCombatReplayPersistenceDrained(CombatReplayPersistenceDrained drained)
    {
        lock (_lifecycleGate)
        {
            if (!_started || _stopped)
                return;
            OnCombatReplayPersistenceDrainedUnderLock();
        }
    }

    private void OnCombatReplayPersistenceDrainedUnderLock()
    {
        string? runId = null;
        try
        {
            runId = _deferredRunCompletionRunId ?? RequireSessionManager().ActiveSession?.RunId;
            if (!_runContext.IsInGameRun)
                TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.replay_drain.handling_failed",
                    storm: []
                ),
                ex,
                ("run_id", runId, BppLogCorrelationPolicy.Short),
                ("reason_code", RunLoggingReasonCode.ReplayDrainHandlingException)
            );
        }
    }

    private void OnRankRefreshed(RankSnapshot rank)
    {
        lock (_lifecycleGate)
        {
            if (!_started || _stopped)
                return;
            OnRankRefreshedUnderLock(rank);
        }
    }

    private void OnRankRefreshedUnderLock(RankSnapshot rank)
    {
        string? runId = null;
        try
        {
            if (_deferredRunCompletion == null || !_deferredRunCompletionAwaitsRank)
                return;
            runId = _deferredRunCompletionRunId;
            _deferredRunCompletion.FinalPlayerRank = rank.Rank;
            _deferredRunCompletion.FinalPlayerRating = rank.Rating;
            _deferredRunCompletionAwaitsRank = false;
            if (!_runContext.IsInGameRun)
                TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.rank_refresh.handling_failed",
                    storm: []
                ),
                ex,
                ("run_id", runId, BppLogCorrelationPolicy.Short),
                ("reason_code", RunLoggingReasonCode.RankRefreshHandlingException)
            );
        }
    }

    private void HandleRunActivation(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return;

        var sessionManager = RequireSessionManager();
        var activeSession = sessionManager.ActiveSession;
        if (
            !string.IsNullOrWhiteSpace(_pendingInterruptedRunId)
            && activeSession != null
            && RunLogRunIdentity.MatchesServerRunId(activeSession.RunId, _pendingInterruptedRunId)
        )
        {
            if (RunLogRunIdentity.MatchesServerRunId(_pendingInterruptedRunId, runId))
            {
                ClearPendingInterruptedRun();
            }
            else
            {
                sessionManager.MarkRunAbandoned(BuildRunLogAbandonment("run_interrupted"));
                _startedEventRunId = null;
                ClearPendingInterruptedRun();
            }
        }

        CancelDeferredRunExitForActivation();
        EnsureActiveRunFromGame();
    }

    private RunLogSessionState? EnsureActiveRunFromGame()
    {
        var basics = ReadRunBasics();
        var rank = ReadRank();
        if (
            !RunLogRecordMapper.TryCreateRunLogCreateRequest(
                basics,
                rank,
                _runContext.CurrentServerRunId,
                _buildChannel,
                _utcNow(),
                out var request
            )
        )
        {
            return null;
        }

        var sessionManager = RequireSessionManager();
        request.PlayerAccountId = _playerAccountIdResolver();
        request.BundleScreenshotRequested = _bundleScreenshotRequestedResolver();
        request.ModVersion = _modVersionResolver();
        var session = sessionManager.EnsureActiveSession(request);
        if (!string.Equals(session.RunId, request.RunId, StringComparison.Ordinal))
        {
            _runContext.CurrentServerRunId = session.RunId;
            if (!string.Equals(_startedEventRunId, session.RunId, StringComparison.Ordinal))
            {
                BppLog.InfoEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.RunLogging,
                        "run_logging.run.id_collision_recovered",
                        storm: []
                    ),
                    ("run_id", session.RunId, BppLogCorrelationPolicy.Short),
                    ("server_run_id", request.RunId, BppLogCorrelationPolicy.Short)
                );
            }
        }
        sessionManager.SetPlayerAccountIdOnce(_playerAccountIdResolver());
        if (string.Equals(_startedEventRunId, session.RunId, StringComparison.Ordinal))
            return session;

        sessionManager.AppendEvent(
            new RunLogEvent
            {
                Kind = session.LastSeq > 0 ? "run_resumed" : "run_started",
                Day = session.Day ?? request.Day,
                Hour = session.Hour ?? request.Hour,
                Hero = request.Hero,
                GameMode = request.GameMode,
            }
        );
        sessionManager.SaveCheckpoint();
        _startedEventRunId = session.RunId;
        return session;
    }

    private RunLogCompletion BuildRunLogCompletion(string reason) =>
        RunLogRecordMapper.BuildRunLogCompletion(
            reason,
            _runContext.LastRunExitKind,
            ReadRunBasics(),
            ReadPlayerStats(),
            ReadRank(),
            _utcNow()
        );

    private RunLogAbandonment BuildRunLogAbandonment(string reason) =>
        RunLogRecordMapper.BuildRunLogAbandonment(reason, ReadRunBasics(), _utcNow());

    private RunBasicsSnapshot? ReadRunBasics() =>
        _snapshotProbe.TryGetRunBasics(out var basics) ? basics : null;

    private PlayerStatsSnapshot? ReadPlayerStats() =>
        _snapshotProbe.TryGetPlayerStats(out var stats) ? stats : null;

    private RankSnapshot? ReadRank() =>
        _snapshotProbe.TryGetRankSnapshot(out var rank) ? rank : null;

    private RunLogSessionState? TryResolveReplayTargetSession(
        PvpBattleRecorded manifest,
        bool inRun
    )
    {
        if (inRun)
        {
            var session = EnsureActiveRunFromGame();
            if (session == null)
                return null;

            if (
                !string.IsNullOrWhiteSpace(manifest.RunId)
                && !RunLogRunIdentity.MatchesServerRunId(session.RunId, manifest.RunId)
            )
            {
                EmitBattleCaptureFailure(manifest, RunLoggingReasonCode.InRunMismatch);
                return null;
            }

            return session;
        }

        var deferredSession = RequireSessionManager().ActiveSession;
        if (deferredSession == null)
            return null;

        if (string.IsNullOrWhiteSpace(manifest.RunId))
        {
            EmitBattleCaptureFailure(
                manifest,
                RunLoggingReasonCode.ManifestRunUnavailable,
                fallbackRunId: deferredSession.RunId
            );
            return null;
        }

        if (!RunLogRunIdentity.MatchesServerRunId(deferredSession.RunId, manifest.RunId))
        {
            EmitBattleCaptureFailure(manifest, RunLoggingReasonCode.DeferredRunMismatch);
            return null;
        }

        return deferredSession;
    }

    private bool TryCompleteDeferredRunExit(bool forceCompletion = false)
    {
        if (_deferredRunCompletion == null)
            return false;

        var sessionManager = RequireSessionManager();
        var activeSession = sessionManager.ActiveSession;
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
        RunLoggingReasonCode? degradationReason = null;
        var replayPending = _hasPendingReplayPersistence();
        var gracePeriod = _deferredRunCompletionAwaitsRank
            ? RankRefreshCompletionGracePeriod
            : ReplayPersistenceCompletionGracePeriod;
        if (!forceCompletion && (replayPending || _deferredRunCompletionAwaitsRank))
        {
            var deadline = _deferredRunCompletionDeadlineUtc ?? (now + gracePeriod);
            _deferredRunCompletionDeadlineUtc = deadline;
            if (now < deadline)
            {
                _deferredRunCompletionTimer ??= _scheduleDeferredCompletion(
                    deadline - now,
                    OnDeferredCompletionDeadline
                );
                return false;
            }

            degradationReason = replayPending
                ? RunLoggingReasonCode.ReplayDrainTimeout
                : RunLoggingReasonCode.RankRefreshTimeout;
        }
        else if (forceCompletion && (replayPending || _deferredRunCompletionAwaitsRank))
        {
            degradationReason = RunLoggingReasonCode.ShutdownForced;
        }

        var completedRunId = activeSession.RunId;
        var completedStatus = _deferredRunCompletion.Status;
        sessionManager.SetPlayerAccountIdOnce(_playerAccountIdResolver());
        sessionManager.CompleteRun(_deferredRunCompletion);
        _startedEventRunId = null;
        ClearDeferredRunCompletion();
        // CompleteRun has drained the write queue and committed the row, so the seal can find it.
        if (string.Equals(completedStatus, "completed", StringComparison.Ordinal))
            _eventBus.Publish(new RunLogRunCompleted { RunId = completedRunId });
        if (degradationReason.HasValue)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.run.completion_degraded",
                    storm: ["reason_code"]
                ),
                ("run_id", completedRunId, BppLogCorrelationPolicy.Short),
                ("reason_code", degradationReason.Value),
                ("grace_ms", (long)gracePeriod.TotalMilliseconds)
            );
        }
        return true;
    }

    private void OnDeferredCompletionDeadline()
    {
        lock (_lifecycleGate)
        {
            if (!_started || _stopped)
                return;

            var firedTimer = _deferredRunCompletionTimer;
            _deferredRunCompletionTimer = null;
            firedTimer?.Dispose();
            if (_runContext.IsInGameRun)
            {
                ClearDeferredRunCompletion();
                return;
            }

            var runId = _deferredRunCompletionRunId ?? RequireSessionManager().ActiveSession?.RunId;
            try
            {
                TryCompleteDeferredRunExit();
            }
            catch (Exception ex)
            {
                BppLog.ErrorEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.RunLogging,
                        "run_logging.replay_drain.handling_failed",
                        storm: []
                    ),
                    ex,
                    ("run_id", runId, BppLogCorrelationPolicy.Short),
                    ("reason_code", RunLoggingReasonCode.ReplayDrainHandlingException)
                );
            }
        }
    }

    private void EmitBattleCaptureFailure(
        PvpBattleRecorded? manifest,
        RunLoggingReasonCode reasonCode,
        Exception? exception = null,
        string? fallbackRunId = null
    )
    {
        var runId = !string.IsNullOrWhiteSpace(manifest?.RunId)
            ? manifest.RunId
            : fallbackRunId
                ?? _sessionManager?.ActiveSession?.RunId
                ?? _deferredRunCompletionRunId
                ?? _runContext.CurrentServerRunId;
        var fields = new BppLogField[]
        {
            ("run_id", runId, BppLogCorrelationPolicy.Short),
            ("battle_id", manifest?.BattleId, BppLogCorrelationPolicy.Short),
            ("reason_code", reasonCode),
        };
        if (exception == null)
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.battle.capture_failed",
                    storm: []
                ),
                fields
            );
        else
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.RunLogging,
                    "run_logging.battle.capture_failed",
                    storm: []
                ),
                exception,
                fields
            );
    }

    private static RunLoggingTransition ToLogTransition(RunLifecycleChanged change)
    {
        if (change.IsInGameRun)
            return RunLoggingTransition.RunEntered;
        if (IsCompletedTransition(change))
            return RunLoggingTransition.RunEnded;
        if (IsInterruptedTransition(change))
            return RunLoggingTransition.RunInterrupted;
        if (string.Equals(change.Reason, "Live run-state reconciliation", StringComparison.Ordinal))
            return RunLoggingTransition.StateReconciled;
        return RunLoggingTransition.Unknown;
    }

    private void CancelDeferredRunExitForActivation()
    {
        if (_deferredRunCompletion == null)
            return;
        ClearDeferredRunCompletion();
    }

    private void ClearDeferredRunCompletion()
    {
        var timer = _deferredRunCompletionTimer;
        _deferredRunCompletionTimer = null;
        _deferredRunCompletion = null;
        _deferredRunCompletionRunId = null;
        _deferredRunCompletionAwaitsRank = false;
        _deferredRunCompletionDeadlineUtc = null;
        timer?.Dispose();
    }

    private RunLogSessionManager RequireSessionManager() =>
        _sessionManager
        ?? throw new InvalidOperationException("Run logging module has not been started.");

    private void ClearPendingInterruptedRun()
    {
        _pendingInterruptedRunId = null;
    }

    private static bool IsCompletedTransition(RunLifecycleChanged change)
    {
        return string.Equals(change.Reason, RunLifecycleReasons.RunEnded, StringComparison.Ordinal);
    }

    private static bool IsInterruptedTransition(RunLifecycleChanged change)
    {
        return string.Equals(
            change.Reason,
            RunLifecycleReasons.RunInterrupted,
            StringComparison.Ordinal
        );
    }
}
