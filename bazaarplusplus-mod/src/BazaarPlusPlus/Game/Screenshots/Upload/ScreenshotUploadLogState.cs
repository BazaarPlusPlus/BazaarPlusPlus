#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Screenshots.Upload;

internal static class ScreenshotUploadLogValues
{
    internal const string Endpoint = "bazaardb_snapshot";
    internal const string InvalidLocalPaths = "invalid_local_paths";
    internal const string RouteUnavailable = "route_unavailable";
    internal const string InitializationException = "initialization_exception";
    internal const string AccountContextUnavailable = "account_context_unavailable";
    internal const string HealthProbeFailed = "health_probe_failed";
    internal const string ServiceException = "service_exception";
}

internal sealed class ScreenshotUploadLogState
{
    private readonly object _sync = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private Degradation? _degradation;

    internal ScreenshotUploadLogState()
        : this(static () => DateTimeOffset.UtcNow) { }

    internal ScreenshotUploadLogState(Func<DateTimeOffset> utcNow)
    {
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    internal void ReportInitializationDegraded(string reasonCode, Exception? exception = null)
    {
        var fields = new[]
        {
            ScreenshotUploadLogEvents.InitializationDegradedReasonCode.Bind(reasonCode),
            ScreenshotUploadLogEvents.InitializationDegradedEndpoint.Bind(
                ScreenshotUploadLogValues.Endpoint
            ),
        };
        if (exception == null)
            BppLog.WarnEvent(ScreenshotUploadLogEvents.InitializationDegraded, fields);
        else
            BppLog.WarnEvent(ScreenshotUploadLogEvents.InitializationDegraded, exception, fields);
    }

    internal void ReportWaiting(int pendingCount)
    {
        BppLog.DebugEvent(
            ScreenshotUploadLogEvents.Waiting,
            () =>
                [
                    ScreenshotUploadLogEvents.WaitingReasonCode.Bind(
                        ScreenshotUploadLogValues.AccountContextUnavailable
                    ),
                    ScreenshotUploadLogEvents.WaitingPendingCount.Bind(pendingCount),
                ]
        );
    }

    internal void ReportHealthDegraded(string reasonCode, long? roundTripMilliseconds)
    {
        lock (_sync)
        {
            if (_degradation.HasValue)
                return;

            _degradation = new Degradation(reasonCode, SafeUtcNow());
        }

        BppLog.WarnEvent(
            ScreenshotUploadLogEvents.Degraded,
            ScreenshotUploadLogEvents.DegradedEndpoint.Bind(ScreenshotUploadLogValues.Endpoint),
            ScreenshotUploadLogEvents.DegradedReasonCode.Bind(reasonCode),
            ScreenshotUploadLogEvents.DegradedRttMilliseconds.Bind(roundTripMilliseconds)
        );
    }

    internal void ReportHealthRecovered()
    {
        Degradation degradation;
        long outageDurationMilliseconds;
        lock (_sync)
        {
            if (!_degradation.HasValue)
                return;

            degradation = _degradation.Value;
            _degradation = null;
            outageDurationMilliseconds = Math.Max(
                0L,
                (long)(SafeUtcNow() - degradation.StartedAtUtc).TotalMilliseconds
            );
        }

        BppLog.RecoverStorm(
            ScreenshotUploadLogEvents.Degraded,
            ScreenshotUploadLogEvents.DegradedReasonCode.Bind(degradation.ReasonCode)
        );
        BppLog.InfoEvent(
            ScreenshotUploadLogEvents.Recovered,
            ScreenshotUploadLogEvents.RecoveredEndpoint.Bind(ScreenshotUploadLogValues.Endpoint),
            ScreenshotUploadLogEvents.RecoveredReasonCode.Bind(degradation.ReasonCode),
            ScreenshotUploadLogEvents.RecoveredOutageDurationMilliseconds.Bind(
                outageDurationMilliseconds
            )
        );
    }

    private DateTimeOffset SafeUtcNow()
    {
        try
        {
            return _utcNow();
        }
        catch
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private readonly record struct Degradation(string ReasonCode, DateTimeOffset StartedAtUtc);
}
