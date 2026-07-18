#nullable enable
using System.Diagnostics;

namespace BazaarPlusPlus.Infrastructure.Logging;

internal sealed class BppLogEmitter
{
    private BppLogPipeline? _pipeline;

    internal void Install(BppLogPipeline pipeline)
    {
        if (pipeline == null)
            return;

        try
        {
            var previous = Interlocked.Exchange(ref _pipeline, pipeline);
            if (previous != null && !ReferenceEquals(previous, pipeline))
                previous.Flush();
        }
        catch
        {
            // Installation must not prevent plugin startup.
        }
    }

    internal void Emit(
        BppLogSeverity severity,
        BppLogEventDefinition definition,
        IReadOnlyList<BppLogFieldValue>? values = null,
        Exception? exception = null
    )
    {
        try
        {
            Volatile.Read(ref _pipeline)?.Emit(severity, definition, values, exception);
        }
        catch
        {
            // The facade must remain no-throw even before installation.
        }
    }

    [Conditional("DEBUG")]
    internal void Debug(BppLogEventDefinition definition, Func<BppLogFieldValue[]> valuesFactory)
    {
        try
        {
            var pipeline = Volatile.Read(ref _pipeline);
            if (pipeline == null || valuesFactory == null)
                return;
            pipeline.Emit(BppLogSeverity.Debug, definition, valuesFactory());
        }
        catch
        {
            // Debug diagnostics never affect feature behavior.
        }
    }

    [Conditional("DEBUG")]
    internal void Debug(
        BppLogEventDefinition definition,
        Exception exception,
        Func<BppLogFieldValue[]> valuesFactory
    )
    {
        try
        {
            var pipeline = Volatile.Read(ref _pipeline);
            if (pipeline == null || valuesFactory == null)
                return;
            pipeline.Emit(BppLogSeverity.Debug, definition, valuesFactory(), exception);
        }
        catch
        {
            // Debug diagnostics never affect feature behavior.
        }
    }

    internal void RecoverStorm(BppLogEventDefinition definition)
    {
        try
        {
            Volatile.Read(ref _pipeline)?.RecoverStorm(definition);
        }
        catch
        {
            // Recovery reporting is best effort.
        }
    }

    internal void RecoverStorm(
        BppLogEventDefinition definition,
        IReadOnlyList<BppLogFieldValue>? values
    )
    {
        try
        {
            Volatile.Read(ref _pipeline)?.RecoverStorm(definition, values);
        }
        catch
        {
            // Recovery reporting is best effort.
        }
    }

    internal void Flush()
    {
        try
        {
            Volatile.Read(ref _pipeline)?.Flush();
        }
        catch
        {
            // Shutdown must continue if the logger is unavailable.
        }
    }
}
