#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.BundlePipeline;

public static class BundlePipelineLog
{
    public static void Info(BppLogEvent logEvent, string runId, string bundleId) =>
        BppLog.InfoEvent(
            logEvent,
            ("run_id", runId, BppLogCorrelationPolicy.Short),
            ("bundle_id", bundleId, BppLogCorrelationPolicy.Short)
        );

    public static void Warn(
        BppLogEvent logEvent,
        string category,
        Exception? exception = null,
        string? runId = null
    )
    {
        var values =
            runId == null
                ? new BppLogField[] { ("category", category) }
                : new BppLogField[]
                {
                    ("run_id", runId, BppLogCorrelationPolicy.Short),
                    ("category", category),
                };
        if (exception == null)
            BppLog.WarnEvent(logEvent, values);
        else
            BppLog.WarnEvent(logEvent, exception, values);
    }
}
