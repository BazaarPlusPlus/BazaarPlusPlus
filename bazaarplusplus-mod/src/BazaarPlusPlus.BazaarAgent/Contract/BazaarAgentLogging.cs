#nullable enable
using System.Diagnostics;

namespace BazaarPlusPlus.BazaarAgent;

public enum BazaarAgentLogSeverity
{
    Debug,
    Info,
    Warning,
    Error,
}

public enum BazaarAgentLogFieldPrivacy
{
    Public,
    UntrustedText,
}

public enum BazaarAgentLogCardinality
{
    Low,
    High,
}

public enum BazaarAgentLogCorrelation
{
    None,
    Short,
}

public enum BazaarAgentLogReasonCode
{
    ActionDispatchException,
    ActionProcessingException,
    ReplaySinkException,
    ReplayProcessorException,
    ReplayInvalidPayload,
    ReplayRejected,
    ReplayUnavailable,
    HttpHandlerException,
    HttpRequestBodyReadException,
    HttpErrorResponseWriteException,
    HttpResponseCloseException,
    RejectedBodyDrainException,
    DecisionLogAppendException,
    ContextBuildException,
    SceneProbeException,
    ClientCacheTypeUnavailable,
    ProfileFieldUnavailable,
    ProfileValuePropertyUnavailable,
    GameBridgeUnavailable,
    ListenerStartException,
    ListenerCleanupIncomplete,
    ListenerAcceptLoopException,
}

public enum BazaarAgentReplayLogAction
{
    Record,
    Continue,
}

public enum BazaarAgentHttpLogRoute
{
    Context,
    Actions,
    ReplayRecord,
    ReplayContinue,
    Unknown,
}

public enum BazaarAgentHttpLogMethod
{
    Get,
    Post,
    Other,
}

public enum BazaarAgentListenerStopPhase
{
    Cancellation,
    ListenerStop,
    ListenerClose,
    ActionQueueDispose,
    ReplayQueueDispose,
}

public sealed class BazaarAgentLogFieldDefinition
{
    internal BazaarAgentLogFieldDefinition(
        string name,
        BazaarAgentLogFieldPrivacy privacy,
        BazaarAgentLogCardinality cardinality,
        BazaarAgentLogCorrelation correlation
    )
    {
        Name = name;
        Privacy = privacy;
        Cardinality = cardinality;
        Correlation = correlation;
    }

    public string Name { get; }
    public BazaarAgentLogFieldPrivacy Privacy { get; }
    public BazaarAgentLogCardinality Cardinality { get; }
    public BazaarAgentLogCorrelation Correlation { get; }

    internal BazaarAgentLogFieldValue Bind(object? value) => new(this, value);
}

public readonly struct BazaarAgentLogFieldValue
{
    internal BazaarAgentLogFieldValue(BazaarAgentLogFieldDefinition field, object? value)
    {
        Field = field;
        Value = value;
    }

    public BazaarAgentLogFieldDefinition Field { get; }
    public object? Value { get; }
}

public sealed class BazaarAgentLogStormPolicy
{
    private readonly IReadOnlyList<BazaarAgentLogFieldDefinition> _keyFields;

    internal BazaarAgentLogStormPolicy(params BazaarAgentLogFieldDefinition[] keyFields)
    {
        var snapshot =
            keyFields == null
                ? Array.Empty<BazaarAgentLogFieldDefinition>()
                : (BazaarAgentLogFieldDefinition[])keyFields.Clone();
        _keyFields = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<BazaarAgentLogFieldDefinition> KeyFields => _keyFields;
}

public sealed class BazaarAgentLogEventDefinition
{
    private readonly IReadOnlyList<BazaarAgentLogFieldDefinition> _fields;

    internal BazaarAgentLogEventDefinition(
        BazaarAgentLogSeverity severity,
        string eventId,
        params BazaarAgentLogFieldDefinition[] fields
    )
    {
        Severity = severity;
        EventId = eventId;
        var snapshot =
            fields == null
                ? Array.Empty<BazaarAgentLogFieldDefinition>()
                : (BazaarAgentLogFieldDefinition[])fields.Clone();
        _fields = Array.AsReadOnly(snapshot);
    }

    internal BazaarAgentLogEventDefinition(
        BazaarAgentLogSeverity severity,
        string eventId,
        BazaarAgentLogStormPolicy stormPolicy,
        params BazaarAgentLogFieldDefinition[] fields
    )
        : this(severity, eventId, fields) => StormPolicy = stormPolicy;

    internal BazaarAgentLogEventDefinition(
        BazaarAgentLogSeverity severity,
        string eventId,
        string recoversEventId,
        params BazaarAgentLogFieldDefinition[] fields
    )
        : this(severity, eventId, fields) => RecoversEventId = recoversEventId;

    public BazaarAgentLogSeverity Severity { get; }
    public string EventId { get; }
    public IReadOnlyList<BazaarAgentLogFieldDefinition> Fields => _fields;
    public BazaarAgentLogStormPolicy? StormPolicy { get; }
    public string? RecoversEventId { get; }
}

public sealed class BazaarAgentLogEvent
{
    private readonly IReadOnlyList<BazaarAgentLogFieldValue> _values;

    internal BazaarAgentLogEvent(
        BazaarAgentLogEventDefinition definition,
        Exception? exception,
        params BazaarAgentLogFieldValue[] values
    )
    {
        Definition = definition;
        Exception = exception;
        var snapshot =
            values == null
                ? Array.Empty<BazaarAgentLogFieldValue>()
                : (BazaarAgentLogFieldValue[])values.Clone();
        _values = Array.AsReadOnly(snapshot);
    }

    public BazaarAgentLogEventDefinition Definition { get; }
    public IReadOnlyList<BazaarAgentLogFieldValue> Values => _values;
    public Exception? Exception { get; }
}

public static class BazaarAgentLoggerExtensions
{
    public static void TryEmit(this IBazaarAgentLogger logger, BazaarAgentLogEvent logEvent)
    {
        try
        {
            logger?.Emit(logEvent);
        }
        catch
        {
            // Operational logging must never alter Agent behavior or mask the original failure.
        }
    }

    [Conditional("DEBUG")]
    public static void TryEmitDebug(
        this IBazaarAgentLogger logger,
        Func<BazaarAgentLogEvent> logEventFactory
    )
    {
        try
        {
            if (logger != null && logEventFactory != null)
                logger.Emit(logEventFactory());
        }
        catch
        {
            // Debug diagnostics are best effort and absent from Release call sites.
        }
    }
}

public enum BazaarAgentLogStormFlushReason
{
    Expired,
    Evicted,
    Recovered,
    Shutdown,
}

public static class BazaarAgentLogRuntimeEvents
{
    private static readonly BazaarAgentLogFieldDefinition StormSourceEvent = new(
        "source_event",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition StormSuppressedCount = new(
        "suppressed_count",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition StormWindowMilliseconds = new(
        "window_ms",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition StormFlushReason = new(
        "flush_reason",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition StormSuppressedDefinition = new(
        BazaarAgentLogSeverity.Info,
        "agent.storm.suppressed",
        StormSourceEvent,
        StormSuppressedCount,
        StormWindowMilliseconds,
        StormFlushReason
    );

    public static BazaarAgentLogEvent StormSuppressed(
        string sourceEvent,
        int suppressedCount,
        long windowMilliseconds,
        BazaarAgentLogStormFlushReason flushReason
    ) =>
        new(
            StormSuppressedDefinition,
            exception: null,
            StormSourceEvent.Bind(sourceEvent),
            StormSuppressedCount.Bind(suppressedCount),
            StormWindowMilliseconds.Bind(windowMilliseconds),
            StormFlushReason.Bind(flushReason)
        );
}

public static class BazaarAgentLogEvents
{
    private static readonly BazaarAgentLogFieldDefinition SnapshotState = new(
        "state",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition SnapshotReadyDefinition = new(
        BazaarAgentLogSeverity.Info,
        "agent.snapshot.ready",
        SnapshotState
    );

    private static readonly BazaarAgentLogFieldDefinition ActionRequestId = new(
        "request_id",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.Short
    );
    private static readonly BazaarAgentLogFieldDefinition ActionKind = new(
        "action_kind",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ActionReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition ActionFailedDefinition = new(
        BazaarAgentLogSeverity.Error,
        "agent.action.failed",
        new BazaarAgentLogStormPolicy(ActionRequestId),
        ActionRequestId,
        ActionKind,
        ActionReasonCode
    );

    private static readonly BazaarAgentLogFieldDefinition ReplayRequestId = new(
        "request_id",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.Short
    );
    private static readonly BazaarAgentLogFieldDefinition ReplayActionKind = new(
        "action_kind",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ReplayBattleId = new(
        "battle_id",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.Short
    );
    private static readonly BazaarAgentLogFieldDefinition ReplayReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition ReplayRequestFailedDefinition = new(
        BazaarAgentLogSeverity.Error,
        "agent.replay_request.failed",
        new BazaarAgentLogStormPolicy(ReplayRequestId, ReplayBattleId),
        ReplayRequestId,
        ReplayActionKind,
        ReplayBattleId,
        ReplayReasonCode
    );

    private static readonly BazaarAgentLogFieldDefinition HttpRequestId = new(
        "request_id",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.Short
    );
    private static readonly BazaarAgentLogFieldDefinition HttpRoute = new(
        "route",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition HttpMethod = new(
        "method",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition HttpReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition HttpRequestFailedDefinition = new(
        BazaarAgentLogSeverity.Error,
        "agent.http_request.failed",
        new BazaarAgentLogStormPolicy(HttpRequestId),
        HttpRequestId,
        HttpRoute,
        HttpMethod,
        HttpReasonCode
    );

    private static readonly BazaarAgentLogFieldDefinition HttpResponseCloseRequestId = new(
        "request_id",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.Short
    );
    private static readonly BazaarAgentLogFieldDefinition HttpResponseCloseRoute = new(
        "route",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition HttpResponseCloseReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition HttpResponseCloseFailedDefinition = new(
        BazaarAgentLogSeverity.Debug,
        "agent.http_response.close_failed",
        HttpResponseCloseRequestId,
        HttpResponseCloseRoute,
        HttpResponseCloseReasonCode
    );

    private static readonly BazaarAgentLogFieldDefinition RejectedBodyDrainRequestId = new(
        "request_id",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.Short
    );
    private static readonly BazaarAgentLogFieldDefinition RejectedBodyDrainRoute = new(
        "route",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition RejectedBodyDrainReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition RejectedBodyDrainStoppedDefinition = new(
        BazaarAgentLogSeverity.Debug,
        "agent.rejected_body_drain.stopped",
        RejectedBodyDrainRequestId,
        RejectedBodyDrainRoute,
        RejectedBodyDrainReasonCode
    );

    private static readonly BazaarAgentLogFieldDefinition HostInitializationReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition HostInitializationFailedDefinition = new(
        BazaarAgentLogSeverity.Error,
        "agent.host.initialization_failed",
        HostInitializationReasonCode
    );

    public static readonly BazaarAgentLogEventDefinition HostInitializedDefinition = new(
        BazaarAgentLogSeverity.Info,
        "agent.host.initialized"
    );

    private static readonly BazaarAgentLogFieldDefinition DecisionLogDecisionId = new(
        "decision_id",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.Short
    );
    private static readonly BazaarAgentLogFieldDefinition DecisionLogRunId = new(
        "run_id",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.Short
    );
    private static readonly BazaarAgentLogFieldDefinition DecisionLogRequestId = new(
        "request_id",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.Short
    );
    private static readonly BazaarAgentLogFieldDefinition DecisionLogReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition DecisionLogAppendFailedDefinition = new(
        BazaarAgentLogSeverity.Error,
        "agent.decision_log.append_failed",
        new BazaarAgentLogStormPolicy(DecisionLogDecisionId, DecisionLogRunId),
        DecisionLogDecisionId,
        DecisionLogRunId,
        DecisionLogRequestId,
        DecisionLogReasonCode
    );

    private static readonly BazaarAgentLogFieldDefinition ContextDegradedReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition ContextDegradedDefinition = new(
        BazaarAgentLogSeverity.Warning,
        "agent.context.degraded",
        new BazaarAgentLogStormPolicy(ContextDegradedReasonCode),
        ContextDegradedReasonCode
    );

    public static readonly BazaarAgentLogEventDefinition ContextRecoveredDefinition = new(
        BazaarAgentLogSeverity.Info,
        "agent.context.recovered",
        "agent.context.degraded"
    );

    private static readonly BazaarAgentLogFieldDefinition SceneProbeSceneName = new(
        "scene_name",
        BazaarAgentLogFieldPrivacy.UntrustedText,
        BazaarAgentLogCardinality.High,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition SceneProbeSceneReady = new(
        "scene_ready",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition SceneProbeAppStateNull = new(
        "app_state_null",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition SceneProbeProfileLoaded = new(
        "profile_loaded",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition SceneProbeStateChangedDefinition = new(
        BazaarAgentLogSeverity.Debug,
        "agent.scene_probe.state_changed",
        SceneProbeSceneName,
        SceneProbeSceneReady,
        SceneProbeAppStateNull,
        SceneProbeProfileLoaded
    );

    private static readonly BazaarAgentLogFieldDefinition SceneProbeDegradedReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition SceneProbeDegradedDefinition = new(
        BazaarAgentLogSeverity.Warning,
        "agent.scene_probe.degraded",
        new BazaarAgentLogStormPolicy(SceneProbeDegradedReasonCode),
        SceneProbeDegradedReasonCode
    );

    public static readonly BazaarAgentLogEventDefinition SceneProbeRecoveredDefinition = new(
        BazaarAgentLogSeverity.Info,
        "agent.scene_probe.recovered",
        "agent.scene_probe.degraded"
    );

    private static readonly BazaarAgentLogFieldDefinition ListenerStartPort = new(
        "port",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ListenerRecoveryPort = new(
        "port",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ListenerDegradedPort = new(
        "port",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ListenerDegradedReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ListenerRestartOldPort = new(
        "old_port",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ListenerRestartNewPort = new(
        "new_port",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition ListenerStartedDefinition = new(
        BazaarAgentLogSeverity.Info,
        "agent.listener.started",
        ListenerStartPort
    );

    public static readonly BazaarAgentLogEventDefinition ListenerRecoveredDefinition = new(
        BazaarAgentLogSeverity.Info,
        "agent.listener.recovered",
        "agent.listener.degraded",
        ListenerRecoveryPort
    );

    public static readonly BazaarAgentLogEventDefinition ListenerDegradedDefinition = new(
        BazaarAgentLogSeverity.Warning,
        "agent.listener.degraded",
        new BazaarAgentLogStormPolicy(ListenerDegradedPort, ListenerDegradedReasonCode),
        ListenerDegradedPort,
        ListenerDegradedReasonCode
    );

    public static readonly BazaarAgentLogEventDefinition ListenerRestartStartedDefinition = new(
        BazaarAgentLogSeverity.Debug,
        "agent.listener.restart_started",
        ListenerRestartOldPort,
        ListenerRestartNewPort
    );

    private static readonly BazaarAgentLogFieldDefinition ListenerStopReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ListenerStopFailedPhaseCount = new(
        "failed_phase_count",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ListenerStopFirstFailedPhase = new(
        "first_failed_phase",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition ListenerStopDegradedDefinition = new(
        BazaarAgentLogSeverity.Warning,
        "agent.listener.stop_degraded",
        new BazaarAgentLogStormPolicy(ListenerStopReasonCode),
        ListenerStopReasonCode,
        ListenerStopFailedPhaseCount,
        ListenerStopFirstFailedPhase
    );

    private static readonly BazaarAgentLogFieldDefinition ListenerFailedPort = new(
        "port",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );
    private static readonly BazaarAgentLogFieldDefinition ListenerFailedReasonCode = new(
        "reason_code",
        BazaarAgentLogFieldPrivacy.Public,
        BazaarAgentLogCardinality.Low,
        BazaarAgentLogCorrelation.None
    );

    public static readonly BazaarAgentLogEventDefinition ListenerFailedDefinition = new(
        BazaarAgentLogSeverity.Error,
        "agent.listener.failed",
        new BazaarAgentLogStormPolicy(),
        ListenerFailedPort,
        ListenerFailedReasonCode
    );

    public static BazaarAgentLogEvent ActionFailed(
        string requestId,
        BazaarAgentActionKind actionKind,
        BazaarAgentLogReasonCode reasonCode,
        Exception? exception = null
    ) =>
        new(
            ActionFailedDefinition,
            exception,
            ActionRequestId.Bind(requestId),
            ActionKind.Bind(actionKind),
            ActionReasonCode.Bind(reasonCode)
        );

    public static BazaarAgentLogEvent SnapshotReady(BazaarAgentRunStateName state) =>
        new(SnapshotReadyDefinition, exception: null, SnapshotState.Bind(state));

    public static BazaarAgentLogEvent ReplayRequestFailed(
        string requestId,
        BazaarAgentReplayControlKind actionKind,
        string? battleId,
        BazaarAgentLogReasonCode reasonCode,
        Exception? exception
    ) =>
        new(
            ReplayRequestFailedDefinition,
            exception,
            ReplayRequestId.Bind(requestId),
            ReplayActionKind.Bind(
                actionKind == BazaarAgentReplayControlKind.Start
                    ? BazaarAgentReplayLogAction.Record
                    : BazaarAgentReplayLogAction.Continue
            ),
            ReplayBattleId.Bind(NormalizeBattleIdForLog(battleId)),
            ReplayReasonCode.Bind(reasonCode)
        );

    public static BazaarAgentLogEvent HttpRequestFailed(
        string requestId,
        BazaarAgentHttpLogRoute route,
        BazaarAgentHttpLogMethod method,
        BazaarAgentLogReasonCode reasonCode,
        Exception exception
    ) =>
        new(
            HttpRequestFailedDefinition,
            exception,
            HttpRequestId.Bind(requestId),
            HttpRoute.Bind(route),
            HttpMethod.Bind(method),
            HttpReasonCode.Bind(reasonCode)
        );

    public static BazaarAgentLogEvent HttpResponseCloseFailed(
        string requestId,
        BazaarAgentHttpLogRoute route,
        Exception exception
    ) =>
        new(
            HttpResponseCloseFailedDefinition,
            exception,
            HttpResponseCloseRequestId.Bind(requestId),
            HttpResponseCloseRoute.Bind(route),
            HttpResponseCloseReasonCode.Bind(BazaarAgentLogReasonCode.HttpResponseCloseException)
        );

    public static BazaarAgentLogEvent RejectedBodyDrainStopped(
        string requestId,
        BazaarAgentHttpLogRoute route,
        Exception exception
    ) =>
        new(
            RejectedBodyDrainStoppedDefinition,
            exception,
            RejectedBodyDrainRequestId.Bind(requestId),
            RejectedBodyDrainRoute.Bind(route),
            RejectedBodyDrainReasonCode.Bind(BazaarAgentLogReasonCode.RejectedBodyDrainException)
        );

    public static BazaarAgentLogEvent HostInitializationFailed() =>
        new(
            HostInitializationFailedDefinition,
            exception: null,
            HostInitializationReasonCode.Bind(BazaarAgentLogReasonCode.GameBridgeUnavailable)
        );

    public static BazaarAgentLogEvent HostInitialized() =>
        new(HostInitializedDefinition, exception: null);

    public static BazaarAgentLogEvent DecisionLogAppendFailed(
        string decisionId,
        string? runId,
        string requestId,
        Exception exception
    ) =>
        new(
            DecisionLogAppendFailedDefinition,
            exception,
            DecisionLogDecisionId.Bind(decisionId),
            DecisionLogRunId.Bind(runId),
            DecisionLogRequestId.Bind(requestId),
            DecisionLogReasonCode.Bind(BazaarAgentLogReasonCode.DecisionLogAppendException)
        );

    public static BazaarAgentLogEvent ContextDegraded(Exception exception) =>
        ContextDegraded(BazaarAgentLogReasonCode.ContextBuildException, exception);

    public static BazaarAgentLogEvent ContextDegraded(
        BazaarAgentLogReasonCode reasonCode,
        Exception? exception = null
    ) => new(ContextDegradedDefinition, exception, ContextDegradedReasonCode.Bind(reasonCode));

    public static BazaarAgentLogEvent ContextRecovered() =>
        new(ContextRecoveredDefinition, exception: null);

    public static BazaarAgentLogEvent SceneProbeStateChanged(
        string sceneName,
        bool sceneReady,
        bool appStateNull,
        bool profileLoaded
    ) =>
        new(
            SceneProbeStateChangedDefinition,
            exception: null,
            SceneProbeSceneName.Bind(sceneName),
            SceneProbeSceneReady.Bind(sceneReady),
            SceneProbeAppStateNull.Bind(appStateNull),
            SceneProbeProfileLoaded.Bind(profileLoaded)
        );

    public static BazaarAgentLogEvent SceneProbeDegraded(
        BazaarAgentLogReasonCode reasonCode,
        Exception? exception = null
    ) =>
        new(SceneProbeDegradedDefinition, exception, SceneProbeDegradedReasonCode.Bind(reasonCode));

    public static BazaarAgentLogEvent SceneProbeRecovered() =>
        new(SceneProbeRecoveredDefinition, exception: null);

    public static BazaarAgentLogEvent ListenerStarted(int port) =>
        new(ListenerStartedDefinition, exception: null, ListenerStartPort.Bind(port));

    public static BazaarAgentLogEvent ListenerRecovered(int port) =>
        new(ListenerRecoveredDefinition, exception: null, ListenerRecoveryPort.Bind(port));

    public static BazaarAgentLogEvent ListenerDegraded(int port, Exception exception) =>
        new(
            ListenerDegradedDefinition,
            exception,
            ListenerDegradedPort.Bind(port),
            ListenerDegradedReasonCode.Bind(BazaarAgentLogReasonCode.ListenerStartException)
        );

    public static BazaarAgentLogEvent ListenerRestartStarted(int oldPort, int newPort) =>
        new(
            ListenerRestartStartedDefinition,
            exception: null,
            ListenerRestartOldPort.Bind(oldPort),
            ListenerRestartNewPort.Bind(newPort)
        );

    public static BazaarAgentLogEvent ListenerStopDegraded(
        int failedPhaseCount,
        BazaarAgentListenerStopPhase firstFailedPhase,
        Exception firstException
    ) =>
        new(
            ListenerStopDegradedDefinition,
            firstException,
            ListenerStopReasonCode.Bind(BazaarAgentLogReasonCode.ListenerCleanupIncomplete),
            ListenerStopFailedPhaseCount.Bind(failedPhaseCount),
            ListenerStopFirstFailedPhase.Bind(firstFailedPhase)
        );

    public static BazaarAgentLogEvent ListenerFailed(int port, Exception exception) =>
        new(
            ListenerFailedDefinition,
            exception,
            ListenerFailedPort.Bind(port),
            ListenerFailedReasonCode.Bind(BazaarAgentLogReasonCode.ListenerAcceptLoopException)
        );

    private static string? NormalizeBattleIdForLog(string? battleId) =>
        Guid.TryParseExact(battleId, "N", out var parsed) ? parsed.ToString("N") : null;
}
