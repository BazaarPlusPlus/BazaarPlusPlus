#nullable enable
using System;
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;

namespace BazaarPlusPlus.Game.RunLifecycle;

internal sealed class RunLifecycleModule : IBppFeature
{
    private readonly IBppEventBus _eventBus;
    private readonly IGameStateProbe _gameStateProbe;
    private readonly IRunContext _runContext;
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
        BppLog.Info("RunLifecycle", "Subscribed to run lifecycle events");
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
        BppLog.Info("RunLifecycle", $"Captured server run id: {observed.RunId}");
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
        SetInGameRun(false, "Run ended");
    }

    private void OnRunInterrupted()
    {
        _runContext.CurrentServerRunId = null;
        _runContext.LastRunExitKind = RunExitKind.Interrupted;
        SetInGameRun(false, "Run interrupted");
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

        BppLog.Debug(
            "RunLifecycle",
            $"{reason}; IsInGameRun={_runContext.IsInGameRun}, appState={AppState.CurrentState?.GetType().Name ?? "null"}, runState={Data.CurrentState?.StateName.ToString() ?? "null"}, hasActiveRun={Data.HasActiveRun}"
        );
    }
}
