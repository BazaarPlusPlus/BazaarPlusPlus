#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.PvpBattles.Persistence;
using BazaarPlusPlus.Game.RunLogging.Models;
using BazaarPlusPlus.Game.RunLogging.Persistence;
using BazaarPlusPlus.Game.RunLogging.Upload;
using UnityEngine;

namespace BazaarPlusPlus.Game.RunLogging;

internal sealed class RunLoggingController : MonoBehaviour
{
    private IBppServices? _services;
    private IRunLogStore? _store;
    private RunLogSessionManager? _sessionManager;
    private RunLogCaptureService? _captureService;
    private RunLoggingControllerCore? _core;
    private RunLoggingModule? _module;

    public RunLogSessionManager? SessionManager => _sessionManager;

    public RunLogCaptureService? CaptureService => _captureService;

    private void Awake()
    {
        // Wait for Initialize() — core logic moved to InitializeCore
    }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeCore();
    }

    private void InitializeCore()
    {
        var services = _services!;
        var runLogDatabasePath =
            services.Paths.RunLogDatabasePath
            ?? throw new InvalidOperationException("Run log database path is not initialized.");
        var sqliteStore = new SqliteRunLogStore(runLogDatabasePath);
        var uploadStore = new RunSyncStateSqliteStore(runLogDatabasePath);
        var battleCatalog = new PvpBattleCatalog(runLogDatabasePath);
        _store = new QueuedRunLogStore(new ReplicatedRunLogStore(sqliteStore, uploadStore));
        _sessionManager = new RunLogSessionManager(_store);
        _sessionManager.RestoreActiveSession();
        _captureService = new RunLogCaptureService();
        _core = new RunLoggingControllerCore(_sessionManager, _captureService);
        _module = new RunLoggingModule(
            services.EventBus,
            services.RunContext,
            _sessionManager,
            _core,
            () => CombatReplayRuntime.Instance?.HasPendingPersistence == true,
            EnsureActiveRunFromGame,
            battleCatalog.AttachToRun,
            buildRunLogAbandonment: RunLoggingGameDataReader.BuildRunLogAbandonment
        );
        _module.Start();
        BppLog.Info(
            "RunLoggingController",
            $"Initialized run logging database: {services.Paths.RunLogDatabasePath}"
        );
    }

    private void OnDestroy()
    {
        _module?.Stop();
        _module = null;
        (_store as IDisposable)?.Dispose();
        _store = null;
    }

    public RunLogSessionState EnsureActiveSession(RunLogCreateRequest request)
    {
        return RequireCore().EnsureRunStarted(request);
    }

    public RunLogEvent? AppendEvent(RunLogEvent entry)
    {
        return RequireSessionManager().AppendEvent(entry);
    }

    public RunLogCheckpoint SaveCheckpoint()
    {
        return RequireSessionManager().SaveCheckpoint();
    }

    public void CompleteRun(RunLogCompletion completion)
    {
        RequireSessionManager().CompleteRun(completion);
    }

    public void MarkRunAbandoned(RunLogAbandonment abandonment)
    {
        RequireSessionManager().MarkRunAbandoned(abandonment);
    }

    private RunLogSessionState? EnsureActiveRunFromGame()
    {
        if (!RunLoggingGameDataReader.TryCreateRunLogCreateRequest(out var request))
            return null;

        return RequireCore().EnsureRunStarted(request);
    }

    private RunLogSessionManager RequireSessionManager()
    {
        return _sessionManager
            ?? throw new InvalidOperationException("Run logging controller is not initialized.");
    }

    private RunLoggingControllerCore RequireCore()
    {
        return _core
            ?? throw new InvalidOperationException(
                "Run logging controller core is not initialized."
            );
    }
}

internal sealed class RunLoggingControllerCore
{
    private readonly RunLogSessionManager _sessionManager;
    private readonly RunLogCaptureService _captureService;
    private string? _startedEventRunId;

    public RunLoggingControllerCore(
        RunLogSessionManager sessionManager,
        RunLogCaptureService captureService
    )
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
    }

    public RunLogSessionState EnsureRunStarted(RunLogCreateRequest request)
    {
        var session = _sessionManager.EnsureActiveSession(request);
        if (string.Equals(_startedEventRunId, session.RunId, StringComparison.Ordinal))
            return session;

        var eventKind = session.LastSeq > 0 ? "run_resumed" : "run_started";
        _sessionManager.AppendEvent(
            new RunLogEvent
            {
                Kind = eventKind,
                Day = session.Day ?? request.Day,
                Hour = session.Hour ?? request.Hour,
                Hero = request.Hero,
                GameMode = request.GameMode,
            }
        );
        _sessionManager.SaveCheckpoint();
        _startedEventRunId = session.RunId;
        return session;
    }

    public RunLogEvent AcceptCombatReplay(RunLogPvpBattleInput input)
    {
        var combatEvent =
            _sessionManager.AppendEvent(_captureService.BuildPvpBattleRecordedEvent(input))
            ?? throw new InvalidOperationException(
                "Combat replay event was unexpectedly suppressed."
            );
        _sessionManager.SaveCheckpoint();
        return combatEvent;
    }

    public void CompleteRun(RunLogCompletion completion)
    {
        _sessionManager.CompleteRun(completion);
        _startedEventRunId = null;
    }
}
