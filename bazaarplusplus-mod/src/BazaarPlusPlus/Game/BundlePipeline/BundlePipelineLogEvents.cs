#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.BundlePipeline;

[BppLogEventSource]
internal static class BundlePipelineLogEvents
{
    internal static readonly BppLogFieldDefinition RunId = new(
        0,
        "run_id",
        BppLogCorrelationPolicy.Short,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition BundleId = new(
        1,
        "bundle_id",
        BppLogCorrelationPolicy.Short,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition Category = new(
        1,
        "category",
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );

    internal static readonly BppLogEventDefinition SealSucceeded = new(
        BppLogFeatureScope.BundlePipeline,
        "bundle_pipeline.seal.succeeded",
        [RunId, BundleId]
    );
    internal static readonly BppLogEventDefinition SealTerminal = new(
        BppLogFeatureScope.BundlePipeline,
        "bundle_pipeline.seal.terminal",
        [RunId, Category],
        new BppLogStormPolicy([Category])
    );
    internal static readonly BppLogEventDefinition ReconcileFailed = new(
        BppLogFeatureScope.BundlePipeline,
        "bundle_pipeline.reconcile.failed",
        [Category],
        new BppLogStormPolicy([Category])
    );
    internal static readonly BppLogEventDefinition UploadSucceeded = new(
        BppLogFeatureScope.BundlePipeline,
        "bundle_pipeline.upload.succeeded",
        [RunId, BundleId]
    );
    internal static readonly BppLogEventDefinition UploadDegraded = new(
        BppLogFeatureScope.BundlePipeline,
        "bundle_pipeline.upload.degraded",
        [RunId, Category],
        new BppLogStormPolicy([Category])
    );
}

internal static class BundlePipelineLog
{
    internal static void Info(BppLogEventDefinition definition, string runId, string bundleId) =>
        BppLog.InfoEvent(
            definition,
            BundlePipelineLogEvents.RunId.Bind(runId),
            BundlePipelineLogEvents.BundleId.Bind(bundleId)
        );

    internal static void Warn(
        BppLogEventDefinition definition,
        string category,
        Exception? exception = null,
        string? runId = null
    )
    {
        var values =
            runId == null
                ? new[] { BundlePipelineLogEvents.Category.Bind(category) }
                : new[]
                {
                    BundlePipelineLogEvents.RunId.Bind(runId),
                    BundlePipelineLogEvents.Category.Bind(category),
                };
        if (exception == null)
            BppLog.WarnEvent(definition, values);
        else
            BppLog.WarnEvent(definition, exception, values);
    }
}
