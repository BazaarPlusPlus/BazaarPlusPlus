#pragma warning disable CS0436
#nullable enable
using System.Diagnostics;
using BazaarPlusPlus.Infrastructure.Logging;
using BepInEx;
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
                new BppLogPipeline(
                    new BppLogEventRenderer(CreateRedactionRoots()),
                    WriteStructuredToLogger,
                    () => DateTimeOffset.UtcNow
                )
            );
        }
        catch
        {
            // Logging installation must never prevent plugin startup.
        }
    }

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Func<BppLogFieldValue[]> valuesFactory
    ) => StructuredEmitter.Debug(definition, valuesFactory);

    [Conditional("DEBUG")]
    public static void DebugEvent(
        BppLogEventDefinition definition,
        Exception exception,
        Func<BppLogFieldValue[]> valuesFactory
    ) => StructuredEmitter.Debug(definition, exception, valuesFactory);

    public static void InfoEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => StructuredEmitter.Emit(BppLogSeverity.Info, definition, values);

    public static void WarnEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => StructuredEmitter.Emit(BppLogSeverity.Warning, definition, values);

    public static void WarnEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => StructuredEmitter.Emit(BppLogSeverity.Warning, definition, values, exception);

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => StructuredEmitter.Emit(BppLogSeverity.Error, definition, values);

    public static void ErrorEvent(
        BppLogEventDefinition definition,
        Exception exception,
        params BppLogFieldValue[] values
    ) => StructuredEmitter.Emit(BppLogSeverity.Error, definition, values, exception);

    public static void RecoverStorm(BppLogEventDefinition definition) =>
        StructuredEmitter.RecoverStorm(definition);

    public static void RecoverStorm(
        BppLogEventDefinition definition,
        params BppLogFieldValue[] values
    ) => StructuredEmitter.RecoverStorm(definition, values);

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

    private static BppLogRedactionRoots CreateRedactionRoots()
    {
        var gameRoot = SafePath(() => Paths.GameRootPath);
        var dataRoot = SafeCombine(gameRoot, "BazaarPlusPlusV4");
        var pluginRoot = SafePath(() => Paths.PluginPath);
        var homeRoot = SafePath(() =>
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        );
        return new BppLogRedactionRoots(gameRoot, dataRoot, pluginRoot, homeRoot);
    }

    private static string? SafePath(Func<string> pathFactory)
    {
        try
        {
            var path = pathFactory();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeCombine(string? root, string child)
    {
        try
        {
            return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, child);
        }
        catch
        {
            return null;
        }
    }
}
