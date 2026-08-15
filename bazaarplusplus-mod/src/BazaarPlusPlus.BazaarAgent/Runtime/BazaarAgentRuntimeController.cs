#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BazaarPlusPlus.BazaarAgent;

public sealed class BazaarAgentRuntimeController : IDisposable
{
    private const double ListenerReconcileDebounceSeconds = 0.5;
    private const double SnapshotPublishIntervalSeconds = 1.5;
    private const double ActionObservationTimeoutSeconds = 10;

    private readonly IBazaarAgentOptions _options;
    private readonly IBazaarAgentContextReader _contextReader;
    private readonly IBazaarAgentActionDispatcher _dispatcher;
    private readonly IBazaarAgentLogger _logger;
    private readonly IBazaarAgentClock _clock;
    private readonly Action? _snapshotPublished;
    private readonly BazaarAgentActivityFeed _activityFeed = new();
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
#if DEBUG
    private BazaarAgentContextCapture? _contextCapture;
#endif
    private BazaarAgentHttpServer? _http;
    private BazaarAgentCommandQueue<BazaarAgentAction>? _queue;
    private PendingActionObservation? _pendingActionObservation;
    private int _currentPort = -1;

    public BazaarAgentRuntimeController(
        IBazaarAgentOptions options,
        IBazaarAgentContextReader contextReader,
        IBazaarAgentActionDispatcher dispatcher,
        IBazaarAgentLogger logger,
        IBazaarAgentClock clock,
        Action? snapshotPublished = null
    )
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _contextReader = contextReader ?? throw new ArgumentNullException(nameof(contextReader));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
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

        var snapshotPublished = false;
        if (_clock.NowSeconds - _lastTickTime >= SnapshotPublishIntervalSeconds)
        {
            _lastTickTime = _clock.NowSeconds;
            var cooldownLeft = ComputeCooldownLeft();
            var context = _contextReader.Build(cooldownLeft);
            var previous = _snapshots.Current;
            var isCombat =
                context.StateName
                is BazaarAgentRunStateName.Combat
                    or BazaarAgentRunStateName.PvpCombat;
            var previousWasCombat =
                previous?.Context.StateName
                is BazaarAgentRunStateName.Combat
                    or BazaarAgentRunStateName.PvpCombat;

            // A battle emits exactly two contexts: a complete opening lineup and the completed
            // result after combat. Never advance the revision for combat-frame health, tooltip,
            // or animation changes. If the opening message has not been captured yet, retain the
            // preceding actionable snapshot until it is available.
            if (!isCombat || (!previousWasCombat && context.LastBattle?.Phase == "starting"))
            {
                var snapshot = _snapshots.Publish(context, out var isFirstSnapshot);
                if (isFirstSnapshot)
                    _logger.TryEmit(BazaarAgentLogEvents.SnapshotReady(context.StateName));
                if (previous is null || snapshot.TickId != previous.TickId)
                {
#if DEBUG
                    TryCaptureContext(snapshot);
#endif
                    _activityFeed.Publish(
                        "context.observed",
                        requestId: "",
                        route: "runtime/context",
                        summary: "Host observed "
                            + snapshot.Context.StateName
                            + " at tick "
                            + snapshot.TickId,
                        tickId: snapshot.TickId,
                        responseJson: JsonConvert.SerializeObject(snapshot.Context, _responseJson)
                    );
                }

                _snapshotPublished?.Invoke();
                snapshotPublished = true;
            }
        }

        TryCompleteActionObservation();
        DrainActionQueue();
        return snapshotPublished;
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
            _http = new BazaarAgentHttpServer(
                desiredPort,
                () => _snapshots.Current,
                _snapshots.GetNextAfter,
                _queue,
                _logger,
                activityFeed: _activityFeed
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
        _pendingActionObservation?.Pending.SetResponse(
            new BazaarAgentServerResponse(503, "{\"error\":\"unavailable\"}")
        );
        _pendingActionObservation = null;
        var report = _http?.Stop() ?? new BazaarAgentListenerStopReport();
        report.Capture(BazaarAgentListenerStopPhase.ActionQueueDispose, () => _queue?.Dispose());

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
        _currentPort = -1;
    }

    private void DrainActionQueue()
    {
        var queue = _queue;
        var snapshot = _snapshots.Current;
        if (queue is null || snapshot is null || _pendingActionObservation is not null)
            return;

        var pending = queue.TryDequeue();
        if (pending is null)
            return;

        // A dequeued command is claimed (its timeout is disarmed), so it MUST be answered here or
        // retained by _pendingActionObservation until confirmation/timeout.
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

    private void TryCompleteActionObservation()
    {
        var observation = _pendingActionObservation;
        if (observation is null)
            return;

        var current = _snapshots.Current;
        switch (
            BazaarAgentActionObservation.Evaluate(
                observation.Baseline.Context,
                current?.Context,
                observation.Action,
                _clock.NowSeconds,
                observation.DeadlineSeconds
            )
        )
        {
            case BazaarAgentActionObservationStatus.Confirmed:
                CompleteActionObservation(observation, current!, confirmed: true);
                break;
            case BazaarAgentActionObservationStatus.TimedOut:
                CompleteActionObservation(
                    observation,
                    current ?? observation.Baseline,
                    confirmed: false
                );
                break;
        }
    }

    private void CompleteActionObservation(
        PendingActionObservation observation,
        BazaarAgentContextSnapshot observed,
        bool confirmed
    )
    {
        _pendingActionObservation = null;
        var body = BuildOkBody(confirmed ? "confirmed" : "accepted");
        observation.Pending.SetResponse(new BazaarAgentServerResponse(confirmed ? 200 : 202, body));
        if (confirmed && _contextReader is IBazaarAgentBattleSummaryAcknowledger acknowledger)
            acknowledger.AcknowledgeLastBattle();
        LogDecision(
            observation.Pending.RequestId,
            observation.DecisionId,
            observation.Baseline,
            observation.Action,
            executed: true,
            error: confirmed ? null : "accepted_but_unconfirmed"
        );
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
            _lastActionTime = _clock.NowSeconds;
            _pendingActionObservation = new PendingActionObservation(
                pending,
                decisionId,
                snapshot,
                action,
                _clock.NowSeconds + ActionObservationTimeoutSeconds
            );
            return;
        }

        var failureResponse = BazaarAgentDispatchFailureMapper.Map(result.FailureKind);
        var dispatchErrorBody = BuildDispatchErrorBody(failureResponse.ErrorCode, result.Error);
        pending.SetResponse(
            new BazaarAgentServerResponse(failureResponse.HttpStatus, dispatchErrorBody)
        );
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

    private string BuildDispatchErrorBody(string errorCode, string? details)
    {
        var envelope = new Dictionary<string, object?> { ["error"] = errorCode };
        if (details is not null)
            envelope["details"] = details;
        return JsonConvert.SerializeObject(envelope, _responseJson);
    }

    private string BuildOkBody(string status)
    {
        return JsonConvert.SerializeObject(new { status }, _responseJson);
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

#if DEBUG
    private void TryCaptureContext(BazaarAgentContextSnapshot snapshot)
    {
        try
        {
            _contextCapture ??= new BazaarAgentContextCapture(_options.DecisionLogRoot);
            _contextCapture.Capture(snapshot);
        }
        catch (Exception ex)
        {
            _logger.TryEmit(
                BazaarAgentLogEvents.ContextCaptureFailed(
                    snapshot.TickId,
                    snapshot.Context.StateName,
                    ex
                )
            );
        }
    }
#endif

    private sealed class PendingActionObservation
    {
        internal PendingActionObservation(
            BazaarAgentPendingCommand<BazaarAgentAction> pending,
            string decisionId,
            BazaarAgentContextSnapshot baseline,
            BazaarAgentAction action,
            double deadlineSeconds
        )
        {
            Pending = pending;
            DecisionId = decisionId;
            Baseline = baseline;
            Action = action;
            DeadlineSeconds = deadlineSeconds;
        }

        internal BazaarAgentPendingCommand<BazaarAgentAction> Pending { get; }
        internal string DecisionId { get; }
        internal BazaarAgentContextSnapshot Baseline { get; }
        internal BazaarAgentAction Action { get; }
        internal double DeadlineSeconds { get; }
    }

    public void Dispose()
    {
        StopListener();
    }
}
