#pragma warning disable CS0436
#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure.Logging;
using BepInEx.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal static class BppLog
{
    private static ManualLogSource? _logger;
    private static readonly BppLogEmitter StructuredEmitter = new();

    public static void Install(ManualLogSource logger)
    {
        if (logger == null)
            return;

        try
        {
            Volatile.Write(ref _logger, logger);
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
        var logger = Volatile.Read(ref _logger);
        if (logger == null)
            throw new InvalidOperationException("The BepInEx logger is not installed.");

        var level = severity switch
        {
            BppLogSeverity.Debug => LogLevel.Debug,
            BppLogSeverity.Info => LogLevel.Info,
            BppLogSeverity.Warning => LogLevel.Warning,
            BppLogSeverity.Error => LogLevel.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(severity)),
        };
        WriteToLoggerCore(logger, level, message);
    }

    private static void WriteToLoggerCore(ManualLogSource logger, LogLevel level, string message)
    {
        switch (level)
        {
            case LogLevel.Debug:
                logger.LogDebug(message);
                return;
            case LogLevel.Info:
                logger.LogInfo(message);
                return;
            case LogLevel.Warning:
                logger.LogWarning(message);
                return;
            case LogLevel.Error:
                logger.LogError(message);
                return;
            default:
                logger.Log(level, message);
                return;
        }
    }
}
