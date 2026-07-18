#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentRuntimeController : IDisposable
{
    private const double ListenerReconcileDebounceSeconds = 0.5;
    private const double SnapshotPublishIntervalSeconds = 1.5;

    private readonly IBazaarAgentOptions _options;
    private readonly IBazaarAgentContextReader _contextReader;
    private readonly IBazaarAgentActionDispatcher _dispatcher;
    private readonly IBazaarAgentReplayControlSink _replaySink;
    private readonly IBazaarAgentLogger _logger;
    private readonly IBazaarAgentClock _clock;
    private readonly Action? _snapshotPublished;
    private readonly BazaarAgentContextSnapshotPublisher _snapshots = new();
    private readonly BazaarAgentListenerLogState _listenerLogState = new();
    private readonly JsonSerializerSettings _responseJson = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    private double _lastTickTime = double.NegativeInfinity;
    private double _lastActionTime = double.NegativeInfinity;
    private double _lastListenerReconcileTime = double.NegativeInfinity;
    private BazaarAgentDecisionLog? _decisionLog;
    private BazaarAgentHttpServer? _http;
    private BazaarAgentCommandQueue<BazaarAgentAction>? _queue;
    private BazaarAgentCommandQueue<BazaarAgentReplayCommand>? _replayQueue;
    private int _currentPort = -1;

    public BazaarAgentRuntimeController(
        IBazaarAgentOptions options,
        IBazaarAgentContextReader contextReader,
        IBazaarAgentActionDispatcher dispatcher,
        IBazaarAgentReplayControlSink replaySink,
        IBazaarAgentLogger logger,
        IBazaarAgentClock clock,
        Action? snapshotPublished = null
    )
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _replaySink = replaySink ?? throw new ArgumentNullException(nameof(replaySink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _snapshotPublished = snapshotPublished;
    }

    public BazaarAgentContextSnapshot? CurrentSnapshot => _snapshots.Current;

    public bool Tick()
    {
        ReconcileListener();
        if (_http is null || _queue is null)
            return false;

        // Replay control commands drain every tick, before the snapshot publish gate below —
        // otherwise record/continue requests would be throttled to the 1.5 s snapshot cadence
        // and time out. This is also the only place replay commands reach the game: nothing in
        // the snapshot path may exit ReplayState.
        DrainReplayControlQueue();

        if (_clock.NowSeconds - _lastTickTime < SnapshotPublishIntervalSeconds)
            return false;
        _lastTickTime = _clock.NowSeconds;

        var cooldownLeft = ComputeCooldownLeft();
        var context = _contextReader.Build(cooldownLeft);
        var snapshot = _snapshots.Publish(context, out var isFirstSnapshot);
        if (isFirstSnapshot)
            _logger.TryEmit(BazaarAgentLogEvents.SnapshotReady(context.StateName));

        _snapshotPublished?.Invoke();

        var pending = _queue.TryDequeue();
        if (pending is not null)
        {
            // A dequeued command is claimed (its timeout is disarmed), so it MUST be answered
            // here — an unhandled exception would leave the HTTP client waiting forever.
            // Answer BEFORE logging: the logger itself can throw (e.g. disk-full inside the
            // BepInEx log listener chain), and that must not swallow the response.
            try
            {
                ProcessPending(pending, snapshot);
            }
            catch (Exception ex)
            {
                pending.SetResponse(new BazaarAgentServerResponse(500, "{\"error\":\"internal\"}"));
                _logger.TryEmit(
                    BazaarAgentLogEvents.ActionFailed(
                        pending.RequestId,
                        pending.Command.ActionKind,
                        BazaarAgentLogReasonCode.ActionProcessingException,
                        ex
                    )
                );
            }
        }

        return true;
    }

    private void ReconcileListener()
    {
        if (_clock.NowSeconds - _lastListenerReconcileTime < ListenerReconcileDebounceSeconds)
            return;
        _lastListenerReconcileTime = _clock.NowSeconds;

        var desiredPort = BazaarAgentRuntimeDefaults.HttpListenerPort;
        var desiredTimeoutMs = BazaarAgentRuntimeDefaults.ActionTimeoutMilliseconds;

        if (_http is not null && desiredPort == _currentPort)
            return;

        if (_http is not null)
        {
            _logger.TryEmitDebug(() =>
                BazaarAgentLogEvents.ListenerRestartStarted(_currentPort, desiredPort)
            );
            StopListener();
        }

        try
        {
            _queue = new BazaarAgentCommandQueue<BazaarAgentAction>(desiredTimeoutMs);
            _replayQueue = new BazaarAgentCommandQueue<BazaarAgentReplayCommand>(
                BazaarAgentRuntimeDefaults.ReplayControlTimeoutMilliseconds
            );
            _http = new BazaarAgentHttpServer(
                desiredPort,
                () => _snapshots.Current,
                _queue,
                _replayQueue,
                _logger
            );
            _http.Start();
            _currentPort = desiredPort;
            _listenerLogState.OnStartSucceeded(desiredPort, _logger);
        }
        catch (Exception ex)
        {
            _listenerLogState.OnStartFailed(desiredPort, ex, _logger);
            StopListener();
        }
    }

    private void StopListener()
    {
        var report = _http?.Stop() ?? new BazaarAgentListenerStopReport();
        report.Capture(BazaarAgentListenerStopPhase.ActionQueueDispose, () => _queue?.Dispose());
        report.Capture(
            BazaarAgentListenerStopPhase.ReplayQueueDispose,
            () => _replayQueue?.Dispose()
        );

        if (report.FirstException is { } firstException)
        {
            _logger.TryEmit(
                BazaarAgentLogEvents.ListenerStopDegraded(
                    report.FailedPhaseCount,
                    report.FirstFailedPhase,
                    firstException
                )
            );
        }

        _http = null;
        _queue = null;
        _replayQueue = null;
        _currentPort = -1;
    }

    private void DrainReplayControlQueue()
    {
        var queue = _replayQueue;
        if (queue is null)
            return;

        // At most ONE command per tick: a Start pays a multi-MB gzip+msgpack decode on the main
        // thread, and replay control is strictly serial anyway — a burst of queued commands
        // (retry storm, requests accumulated during a scene-load stall) must not stack several
        // decodes into a single frame. The next command runs on the next Update, ~one frame later.
        if (queue.TryDequeue() is { } pending)
        {
            // Claimed commands have their timeout disarmed and must always be answered.
            // Answer BEFORE logging — the logger itself can throw.
            try
            {
                BazaarAgentReplayControlProcessor.Process(pending, _replaySink, _logger);
            }
            catch (Exception ex)
            {
                pending.SetResponse(new BazaarAgentServerResponse(500, "{\"error\":\"internal\"}"));
                _logger.TryEmit(
                    BazaarAgentLogEvents.ReplayRequestFailed(
                        pending.RequestId,
                        pending.Command.Kind,
                        pending.Command.BattleId,
                        BazaarAgentLogReasonCode.ReplayProcessorException,
                        ex
                    )
                );
            }
        }
    }

    private void ProcessPending(
        BazaarAgentPendingCommand<BazaarAgentAction> pending,
        BazaarAgentContextSnapshot snapshot
    )
    {
        var action = pending.Command;
        var decisionId = BazaarAgentUlid.New();
        var cooldownLeft = ComputeCooldownLeft();

        var validation = BazaarAgentActionValidator.Validate(snapshot, action, cooldownLeft);
        if (validation.Code != BazaarAgentValidationCode.Ok)
        {
            var errorBody = BazaarAgentResponseJson.BuildValidationErrorBody(validation);
            pending.SetResponse(new BazaarAgentServerResponse(validation.HttpStatus, errorBody));
            LogDecision(
                pending.RequestId,
                decisionId,
                snapshot,
                action,
                executed: false,
                error: validation.Code.ToString()
            );
            return;
        }

        var result = _dispatcher.Execute(action, snapshot);
        if (result.Executed)
        {
            if (action.ActionKind != BazaarAgentActionKind.Wait)
                _lastActionTime = _clock.NowSeconds;

            var okBody = BuildOkBody(decisionId, snapshot, action, executed: true);
            pending.SetResponse(new BazaarAgentServerResponse(200, okBody));
            LogDecision(
                pending.RequestId,
                decisionId,
                snapshot,
                action,
                executed: true,
                error: null
            );
            return;
        }

        var dispatchErrorBody = BuildDispatchErrorBody(result.Error);
        pending.SetResponse(new BazaarAgentServerResponse(500, dispatchErrorBody));
        if (
            result.Diagnostic == BazaarAgentDispatchDiagnostic.DispatcherException
            && result.DiagnosticException is { } diagnosticException
        )
        {
            _logger.TryEmit(
                BazaarAgentLogEvents.ActionFailed(
                    pending.RequestId,
                    action.ActionKind,
                    BazaarAgentLogReasonCode.ActionDispatchException,
                    diagnosticException
                )
            );
        }
        LogDecision(
            pending.RequestId,
            decisionId,
            snapshot,
            action,
            executed: false,
            error: result.Error
        );
    }

    private string BuildDispatchErrorBody(string? details)
    {
        var envelope = new Dictionary<string, object?> { ["error"] = "internal" };
        if (details is not null)
            envelope["details"] = details;
        return JsonConvert.SerializeObject(envelope, _responseJson);
    }

    private string BuildOkBody(
        string decisionId,
        BazaarAgentContextSnapshot snapshot,
        BazaarAgentAction action,
        bool executed
    )
    {
        var payload = new
        {
            schemaVersion = BazaarAgentSchema.Version,
            decisionId,
            executed,
            tickId = snapshot.TickId,
            actionKind = action.ActionKind.ToString(),
        };
        return JsonConvert.SerializeObject(payload, _responseJson);
    }

    private void LogDecision(
        string requestId,
        string decisionId,
        BazaarAgentContextSnapshot snapshot,
        BazaarAgentAction action,
        bool executed,
        string? error
    )
    {
        try
        {
            var log = GetOrCreateDecisionLog();
            log.Append(
                new BazaarAgentDecisionLogEntry
                {
                    Ts = _clock.UtcNowIsoString(),
                    TickId = snapshot.TickId,
                    DecisionId = decisionId,
                    RunId = snapshot.Context.RunId,
                    State = snapshot.Context.StateName.ToString(),
                    Action = action,
                    Executed = executed,
                    Error = error,
                    Reason = action.Reason,
                }
            );
        }
        catch (Exception ex)
        {
            _logger.TryEmit(
                BazaarAgentLogEvents.DecisionLogAppendFailed(
                    decisionId,
                    snapshot.Context.RunId,
                    requestId,
                    ex
                )
            );
        }
    }

    private double ComputeCooldownLeft()
    {
        var elapsed = _clock.NowSeconds - _lastActionTime;
        var remaining = BazaarAgentRuntimeDefaults.ActionMinDelay.TotalSeconds - elapsed;
        return remaining > 0 ? remaining : 0;
    }

    private BazaarAgentDecisionLog GetOrCreateDecisionLog() =>
        _decisionLog ??= new BazaarAgentDecisionLog(_options.DecisionLogRoot);

    public void Dispose()
    {
        StopListener();
    }
}
