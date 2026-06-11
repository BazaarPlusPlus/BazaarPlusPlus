#pragma warning disable CS0436
#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using BepInEx.Logging;

namespace BazaarPlusPlus.Infrastructure;

internal static class BppLog
{
    private const string Prefix = "[BPP]";

    private static ManualLogSource? _logger;
    private static readonly LogRepeatSuppressor Suppressor = new LogRepeatSuppressor(
        writeSink: WriteToLogger,
        formatSummary: (text, _) => Format("Logger", text)
    );

    public static void Install(ManualLogSource logger)
    {
        _logger = logger;
    }

    public static string Format(string component, string message) =>
        $"{Prefix}[{component}] {message}";

    public static string FormatError(string component, string message, Exception ex) =>
        $"{Format(component, message)}{Environment.NewLine}{ex}";

    public static void Debug(string component, string message)
    {
        if (BppBuild.IsDebug)
            Emit(LogLevel.Debug, Format(component, message));
    }

    public static void Info(string component, string message) =>
        Emit(LogLevel.Info, Format(component, message));

    public static void Warn(string component, string message) =>
        Emit(LogLevel.Warning, Format(component, message));

    public static void Error(string component, string message) =>
        Emit(LogLevel.Error, Format(component, message));

    public static void Error(string component, string message, Exception ex) =>
        Emit(LogLevel.Error, FormatError(component, message, ex));

    public static void Flush()
    {
        if (_logger == null)
            return;

        Suppressor.Flush();
    }

    private static void Emit(LogLevel level, string message)
    {
        if (_logger == null)
            return;

        Suppressor.Write((int)level, message);
    }

    private static void WriteToLogger(int level, string message)
    {
        var logger = _logger;
        if (logger == null)
            return;

        var bepLevel = (LogLevel)level;
        switch (bepLevel)
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
                logger.Log(bepLevel, message);
                return;
        }
    }
}
