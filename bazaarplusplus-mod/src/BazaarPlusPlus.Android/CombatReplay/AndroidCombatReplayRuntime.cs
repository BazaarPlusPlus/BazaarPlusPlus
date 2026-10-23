using BazaarPlusPlus.Android.CombatReplay.Bootstrap;
using BazaarPlusPlus.Android.CombatReplay.PlaybackUi;
using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime;
using Il2CppTheBazaar;
using Il2CppTheBazaar.AppFramework;

namespace BazaarPlusPlus.Android.CombatReplay;

/// <summary>
/// The Android host's saved-replay playback, the twin of the playback half of the desktop's
/// <c>CombatReplayRuntime</c> (the host records no video and has no current-replay recording).
/// It holds Core's <see cref="SavedReplayLifecycle"/>, which owns every start, exit and
/// menu-return decision, and executes them: a replay of a saved battle takes the playback lease
/// of the composition's payload gate for its whole session, the start runs on the main thread
/// through <see cref="AndroidReplayBootstrap"/>, <c>ReplayState</c> transitions arrive through
/// <c>Events.StateChanged</c>, and the only exits are the native recap continue and the
/// bootstrapped <c>ReplayState.Exit</c> prefix, which routes into
/// <see cref="TryExitBootstrappedSavedReplayToMenu"/> (ADR-0002). Nothing exits on a tick: the
/// tick only confirms a pending menu return and clears the exit latch once <c>ReplayState</c> is
/// gone. A plain object ticked from the melon's update, not a MonoBehaviour.
/// </summary>
internal sealed class AndroidCombatReplayRuntime : ISavedReplayRuntime, IDisposable
{
    private static readonly TimeSpan SavedReplayCacheLifetime = TimeSpan.FromSeconds(5);
    private readonly IBppServices _services;
    private readonly RunLifecycleModule _runLifecycle;
    private readonly ReplayPayloadOperationGate _gate;
    private readonly SavedReplayCatalog _saved;
    private readonly string _databasePath;
    private readonly ReplayPlaybackPublisher _publisher;
    private readonly AndroidReplayOpponent _opponent = new();
    private readonly SavedReplayLifecycle _savedReplay = new();
    private readonly Dictionary<string, (bool Saved, DateTime At)> _savedReplayCache = new(
        StringComparer.Ordinal
    );
    private ReplayPlaybackLogOperation? _activePlaybackOperation;
    private ReplayPlaybackLogOperation? _completedPlaybackOperationAwaitingExit;
    private ReplayPlaybackLogOperation? _pendingMenuReturnOperation;
    private IDisposable? _activeReplayPayloadLease;
    private Il2CppSystem.Action<StateChangedEvent>? _stateListener;
    private bool _wasInReplayState;
    private bool _disposed;
    private long _starts;
    private long _startFailures;
    private long _exitsHandled;
    private long _menuReturns;

    public AndroidCombatReplayRuntime(
        IBppServices services,
        RunLifecycleModule runLifecycle,
        ReplayPayloadOperationGate gate,
        SavedReplayCatalog saved,
        string databasePath
    )
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _runLifecycle = runLifecycle ?? throw new ArgumentNullException(nameof(runLifecycle));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _saved = saved ?? throw new ArgumentNullException(nameof(saved));
        _databasePath = databasePath ?? string.Empty;
        _publisher = new ReplayPlaybackPublisher(services);
    }

    /// <summary>The runtime the replay patches reach; null before start and after dispose.</summary>
    public static AndroidCombatReplayRuntime? Instance { get; private set; }

    public bool IsSavedReplayPlaybackActive => _savedReplay.IsSavedReplayPlaybackActive;

    public bool IsReplayStartInProgress => _savedReplay.IsReplayStartInProgress;

    public bool HasSavedReplaySession =>
        _activePlaybackOperation != null
        || _pendingMenuReturnOperation != null
        || IsReplayStartInProgress
        || IsSavedReplayPlaybackActive;

    /// <summary>1 while a replay holds the payload gate's playback lease, else 0.</summary>
    public int PayloadLeasesHeld => _activeReplayPayloadLease != null ? 1 : 0;

    public long Starts => Interlocked.Read(ref _starts);

    public long StartFailures => Interlocked.Read(ref _startFailures);

    public long ExitsHandled => Interlocked.Read(ref _exitsHandled);

    public long MenuReturns => Interlocked.Read(ref _menuReturns);

    /// <summary>Main thread: listens for <c>ReplayState</c> transitions and arms the guards.</summary>
    public void Start()
    {
        Instance = this;
        AndroidReplayGuard.IsActiveSource = () =>
            IsReplayStartInProgress || IsSavedReplayPlaybackActive;
        try
        {
            _stateListener =
                DelegateSupport.ConvertDelegate<Il2CppSystem.Action<StateChangedEvent>>(
                    new Action<StateChangedEvent>(OnStateChanged)
                ) ?? throw new InvalidOperationException("Il2CppInterop returned no delegate.");
            Events.StateChanged.AddListener(_stateListener, null);
        }
        catch (Exception exception)
        {
            // Without the listener, the ReplayState-gone poll in Tick still clears the latch, but
            // a native recap exit would not complete its operation: log it for the device run.
            _stateListener = null;
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.replay.state_listener_failed"),
                exception
            );
        }
    }

    /// <summary>Main thread, every frame.</summary>
    public void Tick()
    {
        if (_disposed)
            return;
        using var scope = AndroidMainThread.Host.Enter();
        try
        {
            ObservePendingMenuReturn();
            var inReplayState = IsInReplayState();
            if (!inReplayState)
                _savedReplay.ObserveReplayStateGone();
            if (_wasInReplayState != inReplayState)
            {
                _wasInReplayState = inReplayState;
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.replay.state_observed"),
                    ("in_replay_state", inReplayState),
                    ("session", HasSavedReplaySession),
                    ("scene", AndroidLobbyScene.Describe())
                );
            }
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.replay.tick_failed",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", exception.GetType().Name)
            );
        }
    }

    public bool CanReplaySavedCombats(out string reason)
    {
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
        if (IsInReplayState())
        {
            reason = "A replay is already in progress.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    // History asks on every refresh; the answer is cached briefly so a refresh does not open the
    // DELETE-mode database on the main thread each time (ReplaySaved rechecks the catalog).
    public bool CanReplaySavedBattle(string battleId, out string reason)
    {
        if (string.IsNullOrWhiteSpace(battleId))
        {
            reason = "Select a saved battle to replay.";
            return false;
        }
        if (!CanReplaySavedCombats(out reason))
            return false;
        if (!HasSavedReplayCached(battleId))
        {
            reason = "Replay payload for the selected battle is unavailable.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public bool ReplaySaved(string battleId, bool recordVideo)
    {
        using var scope = AndroidMainThread.Host.Enter();
        _savedReplayCache.Remove(battleId ?? string.Empty);
        var payloadLease = _gate.TryAcquirePlayback();
        if (payloadLease == null)
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
            if (!CanReplaySavedCombats(out _) || !_saved.HasSavedReplay(battleId!))
            {
                LogRequestRejected(
                    CombatReplayPlaybackSource.LocalSaved,
                    ResolveSavedReplayRejectionReason(battleId),
                    battleId
                );
                return false;
            }
            var manifest = _saved.LoadBattle(battleId!);
            if (manifest == null)
            {
                LogRequestRejected(
                    CombatReplayPlaybackSource.LocalSaved,
                    ReplayRequestRejectionReasonCode.ManifestUnavailable,
                    battleId
                );
                return false;
            }
            var payload = _saved.LoadPayload(manifest);
            if (payload == null)
            {
                LogRequestRejected(
                    CombatReplayPlaybackSource.LocalSaved,
                    ReplayRequestRejectionReasonCode.PayloadUnavailable,
                    battleId
                );
                return false;
            }
            var operation = new ReplayPlaybackLogOperation(
                battleId!,
                CombatReplayPlaybackSource.LocalSaved,
                recordVideo: false
            );
            _completedPlaybackOperationAwaitingExit = null;
            _activePlaybackOperation = operation;
            _activeReplayPayloadLease?.Dispose();
            _activeReplayPayloadLease = payloadLease;
            payloadLease = null;
            return Begin(
                manifest,
                payload,
                battleId!,
                CombatReplayPlaybackSource.LocalSaved,
                operation
            );
        }
        finally
        {
            payloadLease?.Dispose();
        }
    }

    // Reads a saved battle under the payload gate, so the sweep cannot evict it mid-read.
    public SavedBattleLoadOutcome TryLoadSavedBattle(
        string battleId,
        out PvpBattleManifest? manifest,
        out PvpReplayPayload? payload
    )
    {
        manifest = null;
        payload = null;
        using var lease = _gate.TryAcquirePlayback();
        if (lease == null)
            return SavedBattleLoadOutcome.Busy;
        return _saved.TryLoad(battleId, out manifest, out payload);
    }

    public bool ReplayLoadedBattle(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        bool recordVideo,
        CombatReplayPlaybackSource source
    )
    {
        if (source == CombatReplayPlaybackSource.CurrentNative)
            throw new ArgumentOutOfRangeException(nameof(source), source, null);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(payload);
        using var scope = AndroidMainThread.Host.Enter();
        if (!CanReplaySavedCombats(out _))
        {
            LogRequestRejected(source, ResolveGeneralReplayRejectionReason(), manifest.BattleId);
            return false;
        }
        var operation = new ReplayPlaybackLogOperation(
            manifest.BattleId,
            source,
            recordVideo: false
        );
        _completedPlaybackOperationAwaitingExit = null;
        _activePlaybackOperation = operation;
        return Begin(manifest, payload, manifest.BattleId, source, operation);
    }

    /// <summary>
    /// The bootstrapped <c>ReplayState.Exit</c> prefix: true when the runtime handled the exit
    /// (the original body is skipped), false to let the game exit as usual.
    /// </summary>
    public static bool TryExitBootstrappedSavedReplayToMenu()
    {
        var instance = Instance;
        if (instance == null || instance._disposed)
            return false;
        using var scope = AndroidMainThread.Host.Enter();
        var now = UnityEngine.Time.realtimeSinceStartup;
        var decision = instance._savedReplay.RequestBootstrappedExit(now, IsInReplayState());
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.replay.exit_requested"),
            ("decision", decision)
        );
        return decision switch
        {
            // Suppressed: report handled so the prefix skips the original body.
            SavedReplayExitRequestDecision.Suppressed => true,
            SavedReplayExitRequestDecision.NotActive => false,
            SavedReplayExitRequestDecision.Proceed => instance.ExitBootstrappedSavedReplayToMenu(
                now
            ),
            _ => false,
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        using var scope = AndroidMainThread.Host.Enter();
        var operation = _activePlaybackOperation;
        if (operation != null)
        {
            var ended = _publisher.PublishEnded("runtime-destroyed", failed: true);
            CompletePlaybackOperation(
                operation,
                ReplayPlaybackEndReasonCode.RuntimeDestroyed,
                ReplayRollbackStatus.NotRequired,
                ended.Succeeded
                    ? ReplayPlaybackReasonCode.StartException
                    : ReplayPlaybackReasonCode.EndedPublishFailed,
                ended.Exception
            );
        }
        _activePlaybackOperation = null;
        _completedPlaybackOperationAwaitingExit = null;
        _activeReplayPayloadLease?.Dispose();
        _activeReplayPayloadLease = null;
        _pendingMenuReturnOperation = null;
        _savedReplay.ClearPendingMenuReturn();
        if (ReferenceEquals(Instance, this))
            Instance = null;
        AndroidReplayGuard.IsActiveSource = null;
        if (_stateListener != null)
        {
            try
            {
                Events.StateChanged.RemoveListener(_stateListener);
            }
            catch
            {
                // The game may already be tearing its events down at quit.
            }
            _stateListener = null;
        }
    }

    private bool Begin(
        PvpBattleManifest manifest,
        PvpReplayPayload payload,
        string battleId,
        CombatReplayPlaybackSource source,
        ReplayPlaybackLogOperation operation
    )
    {
        CombatSequenceMessages sequence;
        try
        {
            sequence = AndroidCombatReplayLoader.Load(payload);
        }
        catch (Exception exception)
        {
            CompletePlaybackOperation(
                operation,
                ReplayPlaybackEndReasonCode.StartFailed,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.StartException,
                exception
            );
            return false;
        }
        AndroidReplayPlaybackUi.ClearBoardUi();
        _savedReplay.OnStartBegun();
        Interlocked.Increment(ref _starts);
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.replay.start_requested"),
            ("battle_id", battleId, BppLogCorrelationPolicy.Short),
            ("source", source),
            ("from_lobby", !AndroidReplayBootstrap.IsBootstrapReady())
        );
        _ = StartReplayAsync(manifest, sequence, battleId, source, operation);
        return true;
    }

    private async Task StartReplayAsync(
        PvpBattleManifest manifest,
        CombatSequenceMessages sequence,
        string battleId,
        CombatReplayPlaybackSource source,
        ReplayPlaybackLogOperation operation
    )
    {
        var attemptedBootstrapFromLobby = false;
        _publisher.BeginSession(battleId, manifest, source, recordVideo: false);
        try
        {
            _opponent.Cleanup(battleId);
            _opponent.ApplySelectedHeroOverride(manifest);
            Data.ResetRunData();
            _runLifecycle.RefreshRunStateFromCurrentState();
            attemptedBootstrapFromLobby = !AndroidReplayBootstrap.IsBootstrapReady();
            var bootstrappedFromLobby = await AndroidReplayBootstrap.EnsureBootstrapReadyAsync(
                battleId
            );
            _savedReplay.OnBootstrapResolved(bootstrappedFromLobby);
            var context = AndroidReplayBootstrap.ResolveDependencies();
            try
            {
                AndroidReplayOpponent.EnsureOpponentIdentity(
                    manifest,
                    sequence.SpawnMessage,
                    operation
                );
                await _opponent.EnsureTemporaryPortraitAsync(manifest, operation);
            }
            catch (Exception exception)
            {
                operation.ReportDegradation(
                    ReplayPlaybackReasonCode.OpponentPortraitUnavailable,
                    exception
                );
            }
            ReplayRunEconomyFallback.ApplyMissingRunEconomy(manifest, _databasePath, operation);
            await AndroidReplayBootstrap.InjectSavedReplayAsync(
                context,
                manifest,
                sequence,
                operation,
                _publisher.PublishStarting
            );
            var interruption = _savedReplay.TakeStartupInterruption();
            if (interruption != null)
                throw new AndroidReplayStartInterruptedException(
                    interruption.Value.ReasonCode,
                    interruption.Value.Exception
                );
            _savedReplay.OnInjectionCommitted();
            operation.MarkStarted();
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _startFailures);
            _savedReplay.OnStartFailed();
            // Cleanup order is explicit on this path (ADR-0003), not shared with state-exit.
            var ended = ReplayPlaybackCleanup.PublishThenCleanup(
                () => _publisher.PublishEnded("start-failed", failed: true),
                (stage, cleanupException) =>
                    LogCleanupFailure(stage, operation.BattleId, cleanupException),
                new ReplayPlaybackCleanupStep(
                    "opponent_portrait",
                    () => _opponent.Cleanup(battleId)
                ),
                new ReplayPlaybackCleanupStep("hero_restore", _opponent.RestoreSelectedHeroOverride)
            );
            var failureReason =
                exception is AndroidReplayPublishException publishException
                    ? publishException.ReasonCode
                : exception is AndroidReplayStartInterruptedException interrupted
                    ? interrupted.ReasonCode
                : ReplayPlaybackReasonCode.StartException;
            var failureException =
                exception is AndroidReplayStartInterruptedException
                    ? exception.InnerException
                    : exception.InnerException ?? exception;
            if (!ended.Succeeded)
            {
                failureReason = ReplayPlaybackReasonCode.EndedPublishFailed;
                failureException = ended.Exception;
            }
            var rollbackStatus = ReplayRollbackStatus.NotRequired;
            if (attemptedBootstrapFromLobby)
            {
                var rollback = await AndroidReplayBootstrap.RollbackBootstrapAsync(operation);
                rollbackStatus = rollback.Succeeded
                    ? ReplayRollbackStatus.Succeeded
                    : ReplayRollbackStatus.Failed;
                if (!rollback.Succeeded)
                {
                    failureReason = ReplayPlaybackReasonCode.BootstrapRollbackFailed;
                    failureException = rollback.Exception;
                }
            }
            CompletePlaybackOperation(
                operation,
                ReplayPlaybackEndReasonCode.StartFailed,
                rollbackStatus,
                failureReason,
                failureException
            );
        }
        finally
        {
            _ = _savedReplay.OnStartFinished();
        }
    }

    private void OnStateChanged(StateChangedEvent data)
    {
        if (_disposed || data == null)
            return;
        using var scope = AndroidMainThread.Host.Enter();
        try
        {
            var wasReplay = data.PreviousState?.TryCast<ReplayState>() != null;
            var isReplay = data.CurrentState?.TryCast<ReplayState>() != null;
            if (!wasReplay || isReplay)
                return;
            HandleReplayStateExited();
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.replay.state_exit_failed"),
                exception
            );
        }
    }

    private void HandleReplayStateExited()
    {
        var now = UnityEngine.Time.realtimeSinceStartup;
        var ownership = _savedReplay.BeginReplayStateExit(now);
        var operation = _activePlaybackOperation ?? _completedPlaybackOperationAwaitingExit;
        _completedPlaybackOperationAwaitingExit = null;
        // Cleanup order is explicit on this path (ADR-0003), not shared with start-failure.
        var ended = ReplayPlaybackCleanup.PublishThenCleanup(
            () => _publisher.PublishEnded("state-exit", failed: ownership.OwnsTerminalByStart),
            (stage, exception) => LogCleanupFailure(stage, operation?.BattleId, exception),
            new ReplayPlaybackCleanupStep("hero_restore", _opponent.RestoreSelectedHeroOverride),
            new ReplayPlaybackCleanupStep(
                "opponent_portrait",
                () => _opponent.Cleanup(operation?.BattleId)
            ),
            new ReplayPlaybackCleanupStep("playback_ui", AndroidReplayPlaybackUi.ClearBoardUi)
        );
        var decision = _savedReplay.OnReplayStateExited(now, ended.Succeeded, ended.Exception);
        if (decision.Kind == SavedReplayStateExitKind.Defer || operation == null)
            return;
        if (decision.Kind == SavedReplayStateExitKind.CompleteNow)
        {
            CompletePlaybackOperation(
                operation,
                decision.EndReasonCode,
                ReplayRollbackStatus.NotRequired,
                decision.FailureReason,
                decision.Exception
            );
            return;
        }
        BeginPendingMenuReturn(
            operation,
            decision.EndReasonCode,
            decision.FailureReason,
            decision.Exception
        );
    }

    // Bootstrapped saved replays leave through this path (the Exit prefix skips the game's own
    // transition), so the ended signal is published here as the state-exit path does.
    private bool ExitBootstrappedSavedReplayToMenu(float now)
    {
        Interlocked.Increment(ref _exitsHandled);
        var operation = _activePlaybackOperation ?? _completedPlaybackOperationAwaitingExit;
        _completedPlaybackOperationAwaitingExit = null;
        var ended = ReplayPlaybackCleanup.PublishThenCleanup(
            () => _publisher.PublishEnded("saved-replay-exit", failed: false),
            (stage, exception) => LogCleanupFailure(stage, operation?.BattleId, exception),
            new ReplayPlaybackCleanupStep("hero_restore", _opponent.RestoreSelectedHeroOverride),
            new ReplayPlaybackCleanupStep(
                "opponent_portrait",
                () => _opponent.Cleanup(operation?.BattleId)
            ),
            new ReplayPlaybackCleanupStep("playback_ui", AndroidReplayPlaybackUi.ClearBoardUi)
        );
        var decision = _savedReplay.OnReplayStateExited(now, ended.Succeeded, ended.Exception);
        if (operation != null && decision.Kind == SavedReplayStateExitKind.BeginMenuReturn)
            BeginPendingMenuReturn(
                operation,
                decision.EndReasonCode,
                decision.FailureReason,
                decision.Exception
            );
        return true;
    }

    private void BeginPendingMenuReturn(
        ReplayPlaybackLogOperation operation,
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayPlaybackReasonCode priorFailureReason,
        Exception? priorException
    )
    {
        // The lifecycle armed the pending window when it decided BeginMenuReturn; a failed
        // dispatch clears it so the tick cannot later time out an operation completed here.
        var dispatch = TryBeginReturnToMainMenu();
        if (!dispatch.Succeeded)
        {
            _savedReplay.OnMenuReturnDispatchFailed();
            _pendingMenuReturnOperation = null;
            CompletePlaybackOperation(
                operation,
                endReasonCode,
                ReplayRollbackStatus.NotRequired,
                ReplayPlaybackReasonCode.MenuReturnFailed,
                dispatch.Exception
            );
            return;
        }
        Interlocked.Increment(ref _menuReturns);
        _pendingMenuReturnOperation = operation;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.replay.menu_return_started"),
            ("battle_id", operation.BattleId, BppLogCorrelationPolicy.Short),
            ("end_reason_code", endReasonCode)
        );
    }

    // The lobby the menu return waits for is the one History's reopen waits for.
    private void ObservePendingMenuReturn()
    {
        var operation = _pendingMenuReturnOperation;
        if (operation == null)
            return;
        var decision = _savedReplay.TickMenuReturn(
            UnityEngine.Time.realtimeSinceStartup,
            AndroidLobbyScene.IsLoaded()
        );
        if (decision.Kind is SavedReplayMenuReturnKind.None or SavedReplayMenuReturnKind.Wait)
            return;
        _pendingMenuReturnOperation = null;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.replay.menu_return_observed"),
            ("battle_id", operation.BattleId, BppLogCorrelationPolicy.Short),
            ("decision", decision.Kind),
            ("scene", AndroidLobbyScene.Describe())
        );
        CompletePlaybackOperation(
            operation,
            decision.EndReasonCode,
            ReplayRollbackStatus.NotRequired,
            decision.FailureReason,
            decision.Exception
        );
    }

    private static (bool Succeeded, Exception? Exception) TryBeginReturnToMainMenu()
    {
        try
        {
            var runManager =
                Services.Get<Il2Cpp.RunManager>()
                ?? throw new InvalidOperationException("RunManager is unavailable.");
            runManager.ReturnToMainMenu();
            return (true, null);
        }
        catch (Exception exception)
        {
            return (false, exception);
        }
    }

    private void CompletePlaybackOperation(
        ReplayPlaybackLogOperation operation,
        ReplayPlaybackEndReasonCode endReasonCode,
        ReplayRollbackStatus rollbackStatus,
        ReplayPlaybackReasonCode failureReasonCode,
        Exception? exception
    )
    {
        operation.Complete(endReasonCode, rollbackStatus, failureReasonCode, exception);
        if (!ReferenceEquals(_activePlaybackOperation, operation))
            return;
        _activePlaybackOperation = null;
        _activeReplayPayloadLease?.Dispose();
        _activeReplayPayloadLease = null;
    }

    private bool HasSavedReplayCached(string battleId)
    {
        var now = DateTime.UtcNow;
        if (
            _savedReplayCache.TryGetValue(battleId, out var cached)
            && now - cached.At < SavedReplayCacheLifetime
        )
            return cached.Saved;
        bool saved;
        try
        {
            saved = _saved.HasSavedReplay(battleId);
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.replay.catalog_read_failed",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", exception.GetType().Name)
            );
            return false;
        }
        if (_savedReplayCache.Count > 256)
            _savedReplayCache.Clear();
        _savedReplayCache[battleId] = (saved, now);
        return saved;
    }

    private ReplayRequestRejectionReasonCode ResolveSavedReplayRejectionReason(string? battleId)
    {
        if (string.IsNullOrWhiteSpace(battleId))
            return ReplayRequestRejectionReasonCode.InvalidBattleId;
        var general = ResolveGeneralReplayRejectionReason();
        if (general != ReplayRequestRejectionReasonCode.RuntimeUnavailable)
            return general;
        return !_saved.HasSavedReplay(battleId)
            ? ReplayRequestRejectionReasonCode.PayloadUnavailable
            : ReplayRequestRejectionReasonCode.RuntimeUnavailable;
    }

    private ReplayRequestRejectionReasonCode ResolveGeneralReplayRejectionReason()
    {
        if (IsReplayStartInProgress)
            return ReplayRequestRejectionReasonCode.ReplayAlreadyStarting;
        if (_services.RunContext.IsInGameRun)
            return ReplayRequestRejectionReasonCode.ActiveRun;
        if (IsInReplayState())
            return ReplayRequestRejectionReasonCode.ReplayAlreadyActive;
        return ReplayRequestRejectionReasonCode.RuntimeUnavailable;
    }

    private static bool IsInReplayState()
    {
        try
        {
            return AppState.CurrentState?.TryCast<ReplayState>() != null;
        }
        catch
        {
            return false;
        }
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
}

internal sealed class AndroidReplayStartInterruptedException(
    ReplayPlaybackReasonCode reasonCode,
    Exception? innerException
) : Exception("ReplayState exited before replay startup completed.", innerException)
{
    public ReplayPlaybackReasonCode ReasonCode { get; } = reasonCode;
}
