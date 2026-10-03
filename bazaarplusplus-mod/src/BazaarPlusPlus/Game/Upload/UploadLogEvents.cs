#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Upload;

internal static class UploadLogEvents
{
    internal static readonly BppLogFieldDefinition FeedDegradedRunId = Field(
        0,
        "run_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition FeedDegradedReasonCode = Field(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition FeedDegraded = new(
        BppLogFeatureScope.Upload,
        "upload.feed.degraded",
        [FeedDegradedRunId, FeedDegradedReasonCode],
        new BppLogStormPolicy([FeedDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition AttemptDeferredReasonCode = Field(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition AttemptDeferredPendingCount = Field(
        1,
        "pending_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition AttemptDeferred = new(
        BppLogFeatureScope.Upload,
        "upload.attempt.deferred",
        [AttemptDeferredReasonCode, AttemptDeferredPendingCount]
    );

    internal static readonly BppLogEventDefinition AttemptStarted = new(
        BppLogFeatureScope.Upload,
        "upload.attempt.started",
        []
    );

    internal static readonly BppLogFieldDefinition CleanupDegradedPhase = Field(
        0,
        "phase",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CleanupDegradedReasonCode = Field(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CleanupDegraded = new(
        BppLogFeatureScope.Upload,
        "upload.cleanup.degraded",
        [CleanupDegradedPhase, CleanupDegradedReasonCode],
        new BppLogStormPolicy([CleanupDegradedPhase, CleanupDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition FeedSkippedReasonCode = Field(
        0,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition FeedSkipped = new(
        BppLogFeatureScope.Upload,
        "upload.feed.skipped",
        [FeedSkippedReasonCode]
    );

    internal static readonly BppLogFieldDefinition ShutdownDrainDegradedTimeoutMs = Field(
        0,
        "timeout_ms",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ShutdownDrainDegradedReasonCode = Field(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition ShutdownDrainDegraded = new(
        BppLogFeatureScope.Upload,
        "upload.shutdown_drain.degraded",
        [ShutdownDrainDegradedTimeoutMs, ShutdownDrainDegradedReasonCode],
        new BppLogStormPolicy([ShutdownDrainDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition FeedRecoveredRunId = Field(
        0,
        "run_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogEventDefinition FeedRecovered = new(
        BppLogFeatureScope.Upload,
        "upload.feed.recovered",
        [FeedRecoveredRunId]
    );

    private static BppLogFieldDefinition Field(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, correlation, cardinality);
}

internal sealed class UploadFeedLogState
{
    private bool _degraded;
    private UploadLogReasonCode? _degradationReason;
    private UploadLogReasonCode? _deferredReason;

    internal void Observe(UploadAttemptResult result)
    {
        if (result == null)
        {
            ReportDegraded(null, UploadLogReasonCode.AttemptException, null);
            return;
        }

        for (var index = 0; index < result.Observations.Count; index++)
            Observe(result.Observations[index]);
    }

    internal void Observe(UploadAttemptObservation observation)
    {
        switch (observation.Kind)
        {
            case UploadAttemptObservationKind.NoWork:
                _deferredReason = null;
                return;
            case UploadAttemptObservationKind.NoHealthSignal:
                _deferredReason = null;
                return;
            case UploadAttemptObservationKind.Deferred:
                if (observation.ReasonCode.HasValue)
                    ReportDeferred(observation.ReasonCode.Value, observation.PendingCount);
                return;
            case UploadAttemptObservationKind.Degraded:
                ReportDegraded(
                    observation.RunId,
                    observation.ReasonCode ?? UploadLogReasonCode.AttemptException,
                    observation.Exception
                );
                return;
            case UploadAttemptObservationKind.Succeeded:
                ReportSucceeded(observation.RunId);
                return;
        }
    }

    internal void ReportDeferred(UploadLogReasonCode reasonCode, int? pendingCount = null)
    {
        if (_deferredReason == reasonCode)
            return;

        _deferredReason = reasonCode;
        BppLog.DebugEvent(
            UploadLogEvents.AttemptDeferred,
            () =>
                [
                    UploadLogEvents.AttemptDeferredReasonCode.Bind(reasonCode),
                    UploadLogEvents.AttemptDeferredPendingCount.Bind(pendingCount),
                ]
        );
    }

    internal void ReportDegraded(
        string? runId,
        UploadLogReasonCode reasonCode,
        Exception? exception
    )
    {
        _deferredReason = null;
        if (_degraded)
            return;

        _degraded = true;
        _degradationReason = reasonCode;
        var fields = new[]
        {
            UploadLogEvents.FeedDegradedRunId.Bind(runId),
            UploadLogEvents.FeedDegradedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.WarnEvent(UploadLogEvents.FeedDegraded, fields);
        else
            BppLog.WarnEvent(UploadLogEvents.FeedDegraded, exception, fields);
    }

    private void ReportSucceeded(string? runId)
    {
        _deferredReason = null;
        if (!_degraded)
            return;

        var degradationReason = _degradationReason;
        _degraded = false;
        _degradationReason = null;
        if (degradationReason.HasValue)
        {
            BppLog.RecoverStorm(
                UploadLogEvents.FeedDegraded,
                UploadLogEvents.FeedDegradedReasonCode.Bind(degradationReason.Value)
            );
        }
        BppLog.InfoEvent(
            UploadLogEvents.FeedRecovered,
            UploadLogEvents.FeedRecoveredRunId.Bind(runId)
        );
    }
}
