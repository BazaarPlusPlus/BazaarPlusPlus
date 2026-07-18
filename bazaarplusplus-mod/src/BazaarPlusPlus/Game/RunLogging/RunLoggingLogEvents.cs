#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.RunLogging;

internal enum RunLoggingReasonCode
{
    TeardownFinalizationException,
    RunActivationException,
    RunTransitionException,
    BattleCaptureException,
    InRunMismatch,
    ManifestRunUnavailable,
    DeferredRunMismatch,
    ReplayDrainHandlingException,
    ReplayDrainTimeout,
    ShutdownForced,
    QueueShutdownDrainTimeout,
    QueueWriteException,
    QueueWorkerTerminatedUnexpectedly,
}

internal enum RunLoggingTransition
{
    RunEntered,
    RunEnded,
    RunInterrupted,
    StateReconciled,
    Unknown,
}

[BppLogEventSource]
internal static class RunLoggingLogEvents
{
    internal static readonly BppLogFieldDefinition DatabasePath = new(
        0,
        "database_path",
        BppLogFieldPrivacy.LocalPath,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition RunId = new(
        0,
        "run_id",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.Short,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition BattleId = new(
        1,
        "battle_id",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.Short,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition FailureReasonCode = new(
        1,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition Transition = new(
        1,
        "transition",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition TransitionFailureReasonCode = new(
        2,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition BattleFailureReasonCode = new(
        2,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition CompletionDegradedReasonCode = new(
        1,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition GraceMilliseconds = new(
        2,
        "grace_ms",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition QueueShutdownTimeoutMilliseconds = new(
        0,
        "timeout_ms",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition QueueShutdownPendingCount = new(
        1,
        "pending_count",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition QueueShutdownReasonCode = new(
        2,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition QueueWriteOperation = new(
        1,
        "operation",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition QueueWriteReasonCode = new(
        2,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition QueueWorkerPendingCount = new(
        0,
        "pending_count",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition QueueWorkerReasonCode = new(
        1,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogEventDefinition StoreReady = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.store.ready",
        new[] { DatabasePath }
    );

    internal static readonly BppLogEventDefinition CompletionFailed = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.run.completion_failed",
        new[] { RunId, FailureReasonCode },
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );

    internal static readonly BppLogEventDefinition ActivationFailed = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.run.activation_failed",
        new[] { RunId, FailureReasonCode },
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );

    internal static readonly BppLogEventDefinition TransitionFailed = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.run.transition_failed",
        new[] { RunId, Transition, TransitionFailureReasonCode },
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );

    internal static readonly BppLogEventDefinition BattleCaptureFailed = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.battle.capture_failed",
        new[] { RunId, BattleId, BattleFailureReasonCode },
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );

    internal static readonly BppLogEventDefinition ReplayDrainHandlingFailed = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.replay_drain.handling_failed",
        new[] { RunId, FailureReasonCode },
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );

    internal static readonly BppLogEventDefinition CompletionDegraded = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.run.completion_degraded",
        new[] { RunId, CompletionDegradedReasonCode, GraceMilliseconds },
        new BppLogStormPolicy(new[] { CompletionDegradedReasonCode })
    );

    internal static readonly BppLogEventDefinition QueueShutdownDegraded = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.queue.shutdown_degraded",
        new[]
        {
            QueueShutdownTimeoutMilliseconds,
            QueueShutdownPendingCount,
            QueueShutdownReasonCode,
        },
        new BppLogStormPolicy(new[] { QueueShutdownReasonCode })
    );

    internal static readonly BppLogEventDefinition QueueWriteFailed = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.queue.write_failed",
        new[] { RunId, QueueWriteOperation, QueueWriteReasonCode },
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );

    internal static readonly BppLogEventDefinition QueueWorkerFailed = new(
        BppLogFeatureScope.RunLogging,
        "run_logging.queue.worker_failed",
        new[] { QueueWorkerPendingCount, QueueWorkerReasonCode },
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );
}
