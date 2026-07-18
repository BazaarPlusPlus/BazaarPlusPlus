#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.RunLifecycle;

internal enum RunLifecycleLogReason
{
    RunStarted,
    RunEnded,
    RunInterrupted,
    StateReconciled,
}

[BppLogEventSource]
internal static class RunLifecycleLogEvents
{
    internal static readonly BppLogFieldDefinition RunId = new(
        0,
        "run_id",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.Short,
        BppLogCardinality.High
    );

    internal static readonly BppLogFieldDefinition StateChangeReasonCode = new(
        0,
        "reason_code",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition IsInGameRun = new(
        1,
        "is_in_game_run",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition AppState = new(
        2,
        "app_state",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition RunState = new(
        3,
        "run_state",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogFieldDefinition HasActiveRun = new(
        4,
        "has_active_run",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogEventDefinition RunStarted = new(
        BppLogFeatureScope.RunLifecycle,
        "run_lifecycle.run.started",
        new[] { RunId }
    );

    internal static readonly BppLogEventDefinition StateChanged = new(
        BppLogFeatureScope.RunLifecycle,
        "run_lifecycle.state.changed",
        new[] { StateChangeReasonCode, IsInGameRun, AppState, RunState, HasActiveRun }
    );
}
