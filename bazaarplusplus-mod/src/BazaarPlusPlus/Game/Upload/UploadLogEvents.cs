#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Upload;

[BppLogEventSource]
internal static class UploadLogEvents
{
    internal static readonly BppLogFieldDefinition FeedDegradedFeed = Field(
        0,
        "feed",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FeedDegradedRunId = Field(
        1,
        "run_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition FeedDegradedReasonCode = Field(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition FeedDegraded = new(
        BppLogFeatureScope.Upload,
        "upload.feed.degraded",
        [FeedDegradedFeed, FeedDegradedRunId, FeedDegradedReasonCode],
        new BppLogStormPolicy([FeedDegradedFeed, FeedDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition FeedArmedFeed = Field(
        0,
        "feed",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FeedArmedRequestTimeoutMs = Field(
        1,
        "request_timeout_ms",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FeedArmedStartupDelayMs = Field(
        2,
        "startup_delay_ms",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FeedArmedRetryIntervalMs = Field(
        3,
        "retry_interval_ms",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition FeedArmed = new(
        BppLogFeatureScope.Upload,
        "upload.feed.armed",
        [
            FeedArmedFeed,
            FeedArmedRequestTimeoutMs,
            FeedArmedStartupDelayMs,
            FeedArmedRetryIntervalMs,
        ]
    );

    internal static readonly BppLogFieldDefinition AttemptDeferredFeed = Field(
        0,
        "feed",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition AttemptDeferredReasonCode = Field(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition AttemptDeferredPendingCount = Field(
        2,
        "pending_count",
        BppLogCardinality.High
    );
    internal static readonly BppLogEventDefinition AttemptDeferred = new(
        BppLogFeatureScope.Upload,
        "upload.attempt.deferred",
        [AttemptDeferredFeed, AttemptDeferredReasonCode, AttemptDeferredPendingCount]
    );

    internal static readonly BppLogFieldDefinition AccountProbeFailedFeed = Field(
        0,
        "feed",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition AccountProbeFailedReasonCode = Field(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition AccountProbeFailed = new(
        BppLogFeatureScope.Upload,
        "upload.account_probe.failed",
        [AccountProbeFailedFeed, AccountProbeFailedReasonCode]
    );

    internal static readonly BppLogFieldDefinition AttemptStartedFeed = Field(
        0,
        "feed",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition AttemptStarted = new(
        BppLogFeatureScope.Upload,
        "upload.attempt.started",
        [AttemptStartedFeed]
    );

    internal static readonly BppLogFieldDefinition CleanupDegradedFeed = Field(
        0,
        "feed",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CleanupDegradedPhase = Field(
        1,
        "phase",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition CleanupDegradedReasonCode = Field(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition CleanupDegraded = new(
        BppLogFeatureScope.Upload,
        "upload.cleanup.degraded",
        [CleanupDegradedFeed, CleanupDegradedPhase, CleanupDegradedReasonCode],
        new BppLogStormPolicy([CleanupDegradedFeed, CleanupDegradedPhase])
    );

    internal static readonly BppLogFieldDefinition FeedSkippedFeed = Field(
        0,
        "feed",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FeedSkippedReasonCode = Field(
        1,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition FeedSkipped = new(
        BppLogFeatureScope.Upload,
        "upload.feed.skipped",
        [FeedSkippedFeed, FeedSkippedReasonCode]
    );

    internal static readonly BppLogFieldDefinition ShutdownDrainDegradedFeed = Field(
        0,
        "feed",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ShutdownDrainDegradedTimeoutMs = Field(
        1,
        "timeout_ms",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition ShutdownDrainDegradedReasonCode = Field(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition ShutdownDrainDegraded = new(
        BppLogFeatureScope.Upload,
        "upload.shutdown_drain.degraded",
        [
            ShutdownDrainDegradedFeed,
            ShutdownDrainDegradedTimeoutMs,
            ShutdownDrainDegradedReasonCode,
        ],
        new BppLogStormPolicy([ShutdownDrainDegradedFeed, ShutdownDrainDegradedReasonCode])
    );

    internal static readonly BppLogFieldDefinition FeedRecoveredFeed = Field(
        0,
        "feed",
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FeedRecoveredRunId = Field(
        1,
        "run_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogEventDefinition FeedRecovered = new(
        BppLogFeatureScope.Upload,
        "upload.feed.recovered",
        [FeedRecoveredFeed, FeedRecoveredRunId]
    );

    internal static readonly BppLogFieldDefinition BundleBuildFailedRunId = Field(
        0,
        "run_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition BundleBuildFailedBattleId = Field(
        1,
        "battle_id",
        BppLogCardinality.High,
        BppLogCorrelationPolicy.Short
    );
    internal static readonly BppLogFieldDefinition BundleBuildFailedReasonCode = Field(
        2,
        "reason_code",
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition BundleBuildFailed = new(
        BppLogFeatureScope.Upload,
        "upload.bundle.build_failed",
        [BundleBuildFailedRunId, BundleBuildFailedBattleId, BundleBuildFailedReasonCode],
        new BppLogStormPolicy(Array.Empty<BppLogFieldDefinition>())
    );

    private static BppLogFieldDefinition Field(
        int order,
        string name,
        BppLogCardinality cardinality,
        BppLogCorrelationPolicy correlation = BppLogCorrelationPolicy.None
    ) => new(order, name, BppLogFieldPrivacy.Public, correlation, cardinality);
}

internal sealed class UploadFeedLogState
{
    private readonly UploadFeedKind _feed;
    private bool _degraded;
    private UploadLogReasonCode? _degradationReason;
    private UploadLogReasonCode? _deferredReason;

    internal UploadFeedLogState(UploadFeedKind feed)
    {
        _feed = feed;
    }

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
                    UploadLogEvents.AttemptDeferredFeed.Bind(_feed),
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
            UploadLogEvents.FeedDegradedFeed.Bind(_feed),
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
                UploadLogEvents.FeedDegradedFeed.Bind(_feed),
                UploadLogEvents.FeedDegradedReasonCode.Bind(degradationReason.Value)
            );
        }
        BppLog.InfoEvent(
            UploadLogEvents.FeedRecovered,
            UploadLogEvents.FeedRecoveredFeed.Bind(_feed),
            UploadLogEvents.FeedRecoveredRunId.Bind(runId)
        );
    }
}

internal sealed class UploadPayloadFailureLogGate
{
    private const int MaximumEntries = 256;
    private readonly Dictionary<PayloadFailureKey, PayloadFailureEntry> _entries = new();
    private long _sequence;

    internal int Count => _entries.Count;

    internal void Report(
        string runId,
        string battleId,
        string fingerprint,
        UploadLogReasonCode reasonCode,
        Exception? exception
    )
    {
        var key = new PayloadFailureKey(runId, battleId);
        if (
            _entries.TryGetValue(key, out var previous)
            && string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal)
        )
        {
            _entries[key] = new PayloadFailureEntry(fingerprint, NextSequence());
            return;
        }

        if (!_entries.ContainsKey(key) && _entries.Count >= MaximumEntries)
            EvictLeastRecentlyUsed();
        _entries[key] = new PayloadFailureEntry(fingerprint, NextSequence());
        var fields = new[]
        {
            UploadLogEvents.BundleBuildFailedRunId.Bind(runId),
            UploadLogEvents.BundleBuildFailedBattleId.Bind(battleId),
            UploadLogEvents.BundleBuildFailedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.ErrorEvent(UploadLogEvents.BundleBuildFailed, fields);
        else
            BppLog.ErrorEvent(UploadLogEvents.BundleBuildFailed, exception, fields);
    }

    internal void Clear(string runId, string battleId)
    {
        _entries.Remove(new PayloadFailureKey(runId, battleId));
    }

    private long NextSequence() => unchecked(++_sequence);

    private void EvictLeastRecentlyUsed()
    {
        var found = false;
        var oldestKey = default(PayloadFailureKey);
        var oldestSequence = long.MaxValue;
        foreach (var pair in _entries)
        {
            if (found && pair.Value.LastTouchedSequence >= oldestSequence)
                continue;
            found = true;
            oldestKey = pair.Key;
            oldestSequence = pair.Value.LastTouchedSequence;
        }
        if (found)
            _entries.Remove(oldestKey);
    }

    private readonly record struct PayloadFailureKey(string RunId, string BattleId);

    private readonly record struct PayloadFailureEntry(
        string Fingerprint,
        long LastTouchedSequence
    );
}
