#pragma warning disable CS0436
#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Infrastructure;

public static class BppLog
{
    private static IBppLogSink? _sink;
    private static readonly BppLogEmitter StructuredEmitter = new();

    public static void Install(IBppLogSink sink)
    {
        if (sink == null)
            return;

        try
        {
            Volatile.Write(ref _sink, sink);
            StructuredEmitter.Install(
                new BppLogPipeline(WriteStructuredToLogger, () => DateTimeOffset.UtcNow)
            );
        }
        catch
        {
            // Logging installation must never prevent plugin startup.
        }
    }

    /// <summary>Debug-build only: the call and its field factory compile out of Release.</summary>
    [Conditional("DEBUG")]
    public static void DebugEvent(BppLogEvent logEvent, Func<BppLogField[]> fieldsFactory) =>
        StructuredEmitter.Debug(logEvent, null, fieldsFactory);

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEvent logEvent,
        Exception exception,
        Func<BppLogField[]> fieldsFactory
    ) => StructuredEmitter.Debug(logEvent, exception, fieldsFactory);

    public static void InfoEvent(BppLogEvent logEvent, params BppLogField[] fields) =>
        StructuredEmitter.Emit(BppLogSeverity.Info, logEvent, fields);

    public static void WarnEvent(BppLogEvent logEvent, params BppLogField[] fields) =>
        StructuredEmitter.Emit(BppLogSeverity.Warning, logEvent, fields);

    public static void WarnEvent(
        BppLogEvent logEvent,
        Exception exception,
        params BppLogField[] fields
    ) => StructuredEmitter.Emit(BppLogSeverity.Warning, logEvent, fields, exception);

    public static void ErrorEvent(BppLogEvent logEvent, params BppLogField[] fields) =>
        StructuredEmitter.Emit(BppLogSeverity.Error, logEvent, fields);

    public static void ErrorEvent(
        BppLogEvent logEvent,
        Exception exception,
        params BppLogField[] fields
    ) => StructuredEmitter.Emit(BppLogSeverity.Error, logEvent, fields, exception);

    /// <summary>
    /// Ends a storm early and writes its summary: every key of the event without fields, or the
    /// one warning key the fields build.
    /// </summary>
    public static void RecoverStorm(BppLogEvent logEvent, params BppLogField[] fields) =>
        StructuredEmitter.RecoverStorm(logEvent, fields);

    public static void Flush() => StructuredEmitter.Flush();

    private static void WriteStructuredToLogger(BppLogSeverity severity, string message)
    {
        var sink = Volatile.Read(ref _sink);
        if (sink == null)
            throw new InvalidOperationException("The log sink is not installed.");

        sink.Write(severity, message);
    }
}
