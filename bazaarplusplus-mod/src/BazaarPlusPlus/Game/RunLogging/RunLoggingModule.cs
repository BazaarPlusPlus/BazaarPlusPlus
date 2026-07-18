#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.RunLog;
using BazaarPlusPlus.Storage.RunLog.Replication;
using BazaarPlusPlus.Storage.Upload;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLoggingModule : IBppFeature
{
    private static readonly TimeSpan ReplayPersistenceCompletionGracePeriod = TimeSpan.FromSeconds(
        2
    );

    private readonly IBppEventBus _eventBus;
    private readonly object _lifecycleGate = new();
    private readonly IRunContext _runContext;
    private readonly IRunSnapshotProbe _snapshotProbe;
    private readonly string _buildChannel;
    private readonly string? _databasePath;
    private readonly Func<IRunLogStore> _storeFactory;
    private readonly IPvpBattleCatalog _battleCatalog;
    private readonly Func<bool> _hasPendingReplayPersistence;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<TimeSpan, Action, IDisposable> _scheduleDeferredCompletion;
    private IDisposable? _storeLifetime;
    private RunLogSessionManager? _sessionManager;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _pvpBattleSubscription;
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _replayPersistenceDrainedSubscription;
    private RunLogCompletion? _deferredRunCompletion;
    private string? _deferredRunCompletionRunId;
    private DateTime? _deferredRunCompletionDeadlineUtc;
    private IDisposable? _deferredRunCompletionTimer;
    private string? _pendingInterruptedRunId;
    private string? _startedEventRunId;
    private bool _started;
    private bool _stopped;

    internal RunLoggingModule(
        IBppServices services,
        IPvpBattleCatalog battleCatalog,
        Func<bool> hasPendingReplayPersistence
    )
        : this(
            services?.EventBus ?? throw new ArgumentNullException(nameof(services)),
            services.RunContext,
            services.RunSnapshot,
            services.GameBuild.Channel.ToString(),
            () => CreateStore(services),
            battleCatalog,
            hasPendingReplayPersistence,
            static () => DateTime.UtcNow,
            services.Paths.RunLogDatabasePath,
            scheduleDeferredCompletion: null
        ) { }

    internal RunLoggingModule(
        IBppEventBus eventBus,
        IRunContext runContext,
        IRunSnapshotProbe snapshotProbe,
        string buildChannel,
        IRunLogStore store,
        IPvpBattleCatalog battleCatalog,
        Func<bool> hasPendingReplayPersistence,
        Func<DateTime>? utcNow = null,
        string? databasePath = null,
        Func<TimeSpan, Action, IDisposable>? scheduleDeferredCompletion = null
    )
        : this(
            eventBus,
            runContext,
            snapshotProbe,
            buildChannel,
            () => store,
            battleCatalog,
            hasPendingReplayPersistence,
            utcNow,
            databasePath,
            scheduleDeferredCompletion
        ) { }

    internal RunLoggingModule(
        IBppEventBus eventBus,
        IRunContext runContext,
        IRunSnapshotProbe snapshotProbe,
        string buildChannel,
        Func<IRunLogStore> storeFactory,
        IPvpBattleCatalog battleCatalog,
        Func<bool> hasPendingReplayPersistence,
        Func<DateTime>? utcNow = null,
        string? databasePath = null,
        Func<TimeSpan, Action, IDisposable>? scheduleDeferredCompletion = null
    )
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        _snapshotProbe = snapshotProbe ?? throw new ArgumentNullException(nameof(snapshotProbe));
        _buildChannel = buildChannel ?? string.Empty;
        _databasePath = databasePath;
        _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
        _battleCatalog = battleCatalog ?? throw new ArgumentNullException(nameof(battleCatalog));
        _hasPendingReplayPersistence =
            hasPendingReplayPersistence
            ?? throw new ArgumentNullException(nameof(hasPendingReplayPersistence));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _scheduleDeferredCompletion =
            scheduleDeferredCompletion ?? ScheduleDeferredCompletionWithTimer;
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
            RunLoggingLogEvents.StoreReady,
            () => new[] { RunLoggingLogEvents.DatabasePath.Bind(_databasePath) }
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
                        RunLoggingLogEvents.CompletionFailed,
                        ex,
                        RunLoggingLogEvents.RunId.Bind(runId),
                        RunLoggingLogEvents.FailureReasonCode.Bind(
                            RunLoggingReasonCode.TeardownFinalizationException
                        )
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
        _replayPersistenceDrainedSubscription?.Dispose();
        _replayPersistenceDrainedSubscription = null;
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
        _pvpBattleSubscription?.Dispose();
        _pvpBattleSubscription = null;
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;
    }

    private static IRunLogStore CreateStore(IBppServices services)
    {
        var sqliteStore = new RunLogStore(services.Paths);
        var uploadStore = new RunSyncStateStore(services.Paths);
        return new QueuedRunLogStore(
            new ReplicatedRunLogStore(sqliteStore, uploadStore),
            new RunLogStoreLoggerBridge()
        );
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
                RunLoggingLogEvents.ActivationFailed,
                ex,
                RunLoggingLogEvents.RunId.Bind(observed.RunId),
                RunLoggingLogEvents.FailureReasonCode.Bind(
                    RunLoggingReasonCode.RunActivationException
                )
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
            TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            BppLog.ErrorEvent(
                RunLoggingLogEvents.TransitionFailed,
                ex,
                RunLoggingLogEvents.RunId.Bind(runId),
                RunLoggingLogEvents.Transition.Bind(transition),
                RunLoggingLogEvents.TransitionFailureReasonCode.Bind(
                    RunLoggingReasonCode.RunTransitionException
                )
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
        PvpBattleManifest? manifest = null;
        try
        {
            manifest = recorded.Manifest;
            var inRun = _runContext.IsInGameRun;
            if (
                manifest == null
                || !string.Equals(manifest.CombatKind, "PVPCombat", StringComparison.Ordinal)
            )
            {
                return;
            }

            var session = TryResolveReplayTargetSession(manifest, inRun);
            if (session == null)
                return;

            manifest.RunId = session.RunId;
            if (!string.IsNullOrWhiteSpace(manifest.BattleId))
                _battleCatalog.AttachToRun(manifest.BattleId, session.RunId);

            var sessionManager = RequireSessionManager();
            sessionManager.AppendEvent(
                new RunLogEvent
                {
                    Kind = "pvp_combat_recorded",
                    Day = manifest.Day,
                    Hour = manifest.Hour,
                    EncounterId = manifest.EncounterId,
                    CombatKind = manifest.CombatKind,
                    BattleId = manifest.BattleId,
                    OpponentName = manifest.Participants.OpponentName,
                }
            );
            sessionManager.SaveCheckpoint();

            if (!inRun)
                TryCompleteDeferredRunExit();
        }
        catch (Exception ex)
        {
            EmitBattleCaptureFailure(manifest, RunLoggingReasonCode.BattleCaptureException, ex);
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
                RunLoggingLogEvents.ReplayDrainHandlingFailed,
                ex,
                RunLoggingLogEvents.RunId.Bind(runId),
                RunLoggingLogEvents.FailureReasonCode.Bind(
                    RunLoggingReasonCode.ReplayDrainHandlingException
                )
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
                out var request
            )
        )
        {
            return null;
        }

        var sessionManager = RequireSessionManager();
        var session = sessionManager.EnsureActiveSession(request);
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
            ReadRank()
        );

    private RunLogAbandonment BuildRunLogAbandonment(string reason) =>
        RunLogRecordMapper.BuildRunLogAbandonment(reason, ReadRunBasics());

    private RunBasicsSnapshot? ReadRunBasics() =>
        _snapshotProbe.TryGetRunBasics(out var basics) ? basics : null;

    private PlayerStatsSnapshot? ReadPlayerStats() =>
        _snapshotProbe.TryGetPlayerStats(out var stats) ? stats : null;

    private RankSnapshot? ReadRank() =>
        _snapshotProbe.TryGetRankSnapshot(out var rank) ? rank : null;

    private RunLogSessionState? TryResolveReplayTargetSession(
        PvpBattleManifest manifest,
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
                && !string.Equals(session.RunId, manifest.RunId, StringComparison.Ordinal)
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

        if (!string.Equals(deferredSession.RunId, manifest.RunId, StringComparison.Ordinal))
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
        if (!forceCompletion && _hasPendingReplayPersistence())
        {
            var deadline =
                _deferredRunCompletionDeadlineUtc ?? (now + ReplayPersistenceCompletionGracePeriod);
            _deferredRunCompletionDeadlineUtc = deadline;
            if (now < deadline)
            {
                _deferredRunCompletionTimer ??= _scheduleDeferredCompletion(
                    deadline - now,
                    OnDeferredCompletionDeadline
                );
                return false;
            }

            degradationReason = RunLoggingReasonCode.ReplayDrainTimeout;
        }
        else if (forceCompletion && _hasPendingReplayPersistence())
        {
            degradationReason = RunLoggingReasonCode.ShutdownForced;
        }

        var completedRunId = activeSession.RunId;
        sessionManager.CompleteRun(_deferredRunCompletion);
        _startedEventRunId = null;
        ClearDeferredRunCompletion();
        if (degradationReason.HasValue)
        {
            BppLog.WarnEvent(
                RunLoggingLogEvents.CompletionDegraded,
                RunLoggingLogEvents.RunId.Bind(completedRunId),
                RunLoggingLogEvents.CompletionDegradedReasonCode.Bind(degradationReason.Value),
                RunLoggingLogEvents.GraceMilliseconds.Bind(
                    (long)ReplayPersistenceCompletionGracePeriod.TotalMilliseconds
                )
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
                    RunLoggingLogEvents.ReplayDrainHandlingFailed,
                    ex,
                    RunLoggingLogEvents.RunId.Bind(runId),
                    RunLoggingLogEvents.FailureReasonCode.Bind(
                        RunLoggingReasonCode.ReplayDrainHandlingException
                    )
                );
            }
        }
    }

    private void EmitBattleCaptureFailure(
        PvpBattleManifest? manifest,
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
        var fields = new[]
        {
            RunLoggingLogEvents.RunId.Bind(runId),
            RunLoggingLogEvents.BattleId.Bind(manifest?.BattleId),
            RunLoggingLogEvents.BattleFailureReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.ErrorEvent(RunLoggingLogEvents.BattleCaptureFailed, fields);
        else
            BppLog.ErrorEvent(RunLoggingLogEvents.BattleCaptureFailed, exception, fields);
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
