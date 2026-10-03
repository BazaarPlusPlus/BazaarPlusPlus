#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.Upload;

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
            new BppLogEvent(BppLogFeatureScope.Upload, "upload.attempt.deferred"),
            () => [("reason_code", reasonCode), ("pending_count", pendingCount)]
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
        var fields = new BppLogField[]
        {
            ("run_id", runId, BppLogCorrelationPolicy.Short),
            ("reason_code", reasonCode),
        };
        if (exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Upload,
                    "upload.feed.degraded",
                    storm: ["reason_code"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Upload,
                    "upload.feed.degraded",
                    storm: ["reason_code"]
                ),
                exception,
                fields
            );
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
                new BppLogEvent(
                    BppLogFeatureScope.Upload,
                    "upload.feed.degraded",
                    storm: ["reason_code"]
                ),
                ("reason_code", degradationReason.Value)
            );
        }
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Upload, "upload.feed.recovered"),
            ("run_id", runId, BppLogCorrelationPolicy.Short)
        );
    }
}
