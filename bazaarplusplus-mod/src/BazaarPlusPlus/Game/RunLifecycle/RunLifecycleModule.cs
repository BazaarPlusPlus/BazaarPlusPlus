#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus.Game.RunLifecycle;

internal sealed class RunLifecycleModule : IBppFeature
{
    private const int MaximumRecentRunIds = 256;
    private readonly IBppEventBus _eventBus;
    private readonly IGameStateProbe _gameStateProbe;
    private readonly IRunContext _runContext;
    private readonly HashSet<string> _loggedRunIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _loggedRunIdOrder = new();
    private IDisposable? _runInitializedSubscription;

    public RunLifecycleModule(
        IBppEventBus eventBus,
        IGameStateProbe gameStateProbe,
        IRunContext runContext
    )
    {
        _eventBus = eventBus;
        _gameStateProbe = gameStateProbe;
        _runContext = runContext;
    }

    public void Start()
    {
        Events.RunStarted.AddListener(OnRunStarted, null);
        Events.RunEnded.AddListener(OnRunEnded, null);
        Events.RunInterrupted.AddListener(OnRunInterrupted, null);
        _runInitializedSubscription = _eventBus.Subscribe<RunInitializedObserved>(
            OnRunInitializedObserved
        );
    }

    public void Stop()
    {
        Events.RunStarted.RemoveListener(OnRunStarted);
        Events.RunEnded.RemoveListener(OnRunEnded);
        Events.RunInterrupted.RemoveListener(OnRunInterrupted);
        _runInitializedSubscription?.Dispose();
        _runInitializedSubscription = null;
    }

    public void RefreshRunStateFromCurrentState()
    {
        SetInGameRun(_gameStateProbe.ComputeIsInGameRun(), "Live run-state reconciliation");
    }

    public void SetCurrentServerRunId(string? runId)
    {
        _runContext.CurrentServerRunId = runId;
    }

    private void OnRunInitializedObserved(RunInitializedObserved observed)
    {
        SetCurrentServerRunId(observed.RunId);
        if (string.IsNullOrWhiteSpace(observed.RunId))
            return;
        if (!_loggedRunIds.Add(observed.RunId))
            return;

        _loggedRunIdOrder.Enqueue(observed.RunId);
        if (_loggedRunIdOrder.Count > MaximumRecentRunIds)
            _loggedRunIds.Remove(_loggedRunIdOrder.Dequeue());
        BppLog.InfoEvent(
            RunLifecycleLogEvents.RunStarted,
            RunLifecycleLogEvents.RunId.Bind(observed.RunId)
        );
    }

    private void OnRunStarted()
    {
        _runContext.LastRunExitKind = RunExitKind.Completed;
        SetInGameRun(true, "Run started");
    }

    private void OnRunEnded()
    {
        _runContext.CurrentServerRunId = null;
        _runContext.LastRunExitKind = RunExitKind.Completed;
        SetInGameRun(false, RunLifecycleReasons.RunEnded);
    }

    private void OnRunInterrupted()
    {
        _runContext.CurrentServerRunId = null;
        _runContext.LastRunExitKind = RunExitKind.Interrupted;
        SetInGameRun(false, RunLifecycleReasons.RunInterrupted);
    }

    private void SetInGameRun(bool inGameRun, string reason)
    {
        if (_runContext.IsInGameRun == inGameRun)
            return;

        if (!inGameRun)
            _runContext.CurrentServerRunId = null;

        _runContext.IsInGameRun = inGameRun;
        _eventBus.Publish(
            new RunLifecycleChanged
            {
                IsInGameRun = inGameRun,
                LastRunExitKind = _runContext.LastRunExitKind,
                Reason = reason,
            }
        );

        BppLog.DebugEvent(
            RunLifecycleLogEvents.StateChanged,
            () =>
                new[]
                {
                    RunLifecycleLogEvents.StateChangeReasonCode.Bind(ToLogReason(reason)),
                    RunLifecycleLogEvents.IsInGameRun.Bind(_runContext.IsInGameRun),
                    RunLifecycleLogEvents.AppState.Bind(
                        AppState.CurrentState?.GetType().Name ?? "null"
                    ),
                    RunLifecycleLogEvents.RunState.Bind(
                        Data.CurrentState?.StateName.ToString() ?? "null"
                    ),
                    RunLifecycleLogEvents.HasActiveRun.Bind(Data.HasActiveRun),
                }
        );
    }

    private static RunLifecycleLogReason ToLogReason(string reason)
    {
        if (string.Equals(reason, "Run started", StringComparison.Ordinal))
            return RunLifecycleLogReason.RunStarted;
        if (string.Equals(reason, RunLifecycleReasons.RunEnded, StringComparison.Ordinal))
            return RunLifecycleLogReason.RunEnded;
        if (string.Equals(reason, RunLifecycleReasons.RunInterrupted, StringComparison.Ordinal))
            return RunLifecycleLogReason.RunInterrupted;
        return RunLifecycleLogReason.StateReconciled;
    }
}
