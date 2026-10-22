#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.RunLifecycle;

/// <summary>
/// Owns whether the player is in a run and which server run id is current: it follows the game's
/// run lifecycle signals through the host's <see cref="IRunLifecycleSource"/> and the observed run
/// initializations, keeps <see cref="IRunContext"/>, and publishes <see cref="RunLifecycleChanged"/>.
/// </summary>
public sealed class RunLifecycleModule : IBppFeature
{
    private const int MaximumRecentRunIds = 256;
    private readonly IBppEventBus _eventBus;
    private readonly IGameStateProbe _gameStateProbe;
    private readonly IRunContext _runContext;
    private readonly IRunLifecycleSource _source;
    private readonly HashSet<string> _loggedRunIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _loggedRunIdOrder = new();
    private IDisposable? _runInitializedSubscription;
    private IDisposable? _sourceSubscription;

    public RunLifecycleModule(
        IBppEventBus eventBus,
        IGameStateProbe gameStateProbe,
        IRunContext runContext,
        IRunLifecycleSource source
    )
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _gameStateProbe = gameStateProbe ?? throw new ArgumentNullException(nameof(gameStateProbe));
        _runContext = runContext ?? throw new ArgumentNullException(nameof(runContext));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public void Start()
    {
        _sourceSubscription = _source.Subscribe(OnRunStarted, OnRunEnded, OnRunInterrupted);
        _runInitializedSubscription = _eventBus.Subscribe<RunInitializedObserved>(
            OnRunInitializedObserved
        );
    }

    public void Stop()
    {
        _sourceSubscription?.Dispose();
        _sourceSubscription = null;
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
            new BppLogEvent(BppLogFeatureScope.RunLifecycle, "run_lifecycle.run.started"),
            ("run_id", observed.RunId, BppLogCorrelationPolicy.Short)
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
            new BppLogEvent(BppLogFeatureScope.RunLifecycle, "run_lifecycle.state.changed"),
            () =>
                new BppLogField[]
                {
                    ("reason_code", ToLogReason(reason)),
                    ("is_in_game_run", _runContext.IsInGameRun),
                }
                    .Concat(_source.DescribeState())
                    .ToArray()
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
