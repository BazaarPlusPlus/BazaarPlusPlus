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
        BppLogEvent logEvent,
        IReadOnlyList<BppLogField>? fields = null,
        Exception? exception = null
    )
    {
        try
        {
            Volatile.Read(ref _pipeline)?.Emit(severity, logEvent, fields, exception);
        }
        catch
        {
            // The facade must remain no-throw even before installation.
        }
    }

    [Conditional("DEBUG")]
    internal void Debug(
        BppLogEvent logEvent,
        Exception? exception,
        Func<BppLogField[]> fieldsFactory
    )
    {
        try
        {
            var pipeline = Volatile.Read(ref _pipeline);
            if (pipeline == null || fieldsFactory == null)
                return;
            pipeline.Emit(BppLogSeverity.Debug, logEvent, fieldsFactory(), exception);
        }
        catch
        {
            // Debug diagnostics never affect feature behavior.
        }
    }

    internal void RecoverStorm(BppLogEvent logEvent, IReadOnlyList<BppLogField>? fields)
    {
        try
        {
            var pipeline = Volatile.Read(ref _pipeline);
            if (fields == null || fields.Count == 0)
                pipeline?.RecoverStorm(logEvent);
            else
                pipeline?.RecoverStorm(logEvent, fields);
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
