#pragma warning disable CS0436
#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.Runtime;
using BepInEx.Logging;

namespace BazaarPlusPlus;

internal static class BppLog
{
    private const string Prefix = "[BPP]";
    private static readonly object SyncRoot = new object();
    private static readonly List<BufferedLogEntry> RecentEntries = new List<BufferedLogEntry>();
    private static readonly List<BufferedLogEntry> ActiveSequenceBuffer =
        new List<BufferedLogEntry>();

    private static List<BufferedLogEntry>? _activeSequence;
    private static int _activeSequenceIndex;
    private static int _activeSequenceRepeatCount;

    private static ManualLogSource? Logger => BppRuntimeHost.Logger;

    private readonly struct BufferedLogEntry
    {
        public BufferedLogEntry(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        public LogLevel Level { get; }

        public string Message { get; }

        public bool Matches(LogLevel level, string message)
        {
            return Level == level && string.Equals(Message, message, StringComparison.Ordinal);
        }
    }

    public static string Format(string component, string message)
    {
        return $"{Prefix}[{component}] {message}";
    }

    public static string FormatError(string component, string message, Exception ex)
    {
        return $"{Format(component, message)}{Environment.NewLine}{ex}";
    }

    public static void Debug(string component, string message)
    {
        if (BppBuild.IsDebug)
            Write(LogLevel.Debug, Format(component, message));
    }

    public static void Info(string component, string message)
    {
        Write(LogLevel.Info, Format(component, message));
    }

    public static void Warn(string component, string message)
    {
        Write(LogLevel.Warning, Format(component, message));
    }

    public static void Error(string component, string message)
    {
        Write(LogLevel.Error, Format(component, message));
    }

    public static void Error(string component, string message, Exception ex)
    {
        Write(LogLevel.Error, FormatError(component, message, ex));
    }

    public static void Flush()
    {
        var logger = Logger;
        if (logger == null)
            return;

        lock (SyncRoot)
        {
            FlushPendingState(logger);
            RecentEntries.Clear();
        }
    }

    private static void Write(LogLevel level, string message)
    {
        var logger = Logger;
        if (logger == null)
            return;

        level = NormalizeLevel(level);

        lock (SyncRoot)
        {
            var flushedActiveSequence = false;
            if (TryConsumeActiveSequence(logger, level, message, ref flushedActiveSequence))
            {
                return;
            }

            if (flushedActiveSequence)
            {
                Log(logger, level, message);
                RememberEntry(new BufferedLogEntry(level, message));
                return;
            }

            if (TryStartRepeatedSequence(level, message))
            {
                return;
            }

            Log(logger, level, message);
            RememberEntry(new BufferedLogEntry(level, message));
        }
    }

    private static LogLevel NormalizeLevel(LogLevel level)
    {
        return level;
    }

    private static bool TryConsumeActiveSequence(
        ManualLogSource logger,
        LogLevel level,
        string message,
        ref bool flushedActiveSequence
    )
    {
        if (_activeSequence == null || _activeSequence.Count == 0)
            return false;

        var expectedEntry = _activeSequence[_activeSequenceIndex];
        if (!expectedEntry.Matches(level, message))
        {
            FlushPendingState(logger);
            flushedActiveSequence = true;
            return false;
        }

        if (_activeSequence.Count > 1)
        {
            ActiveSequenceBuffer.Add(new BufferedLogEntry(level, message));
        }

        _activeSequenceIndex++;
        if (_activeSequenceIndex < _activeSequence.Count)
        {
            return true;
        }

        _activeSequenceIndex = 0;
        _activeSequenceRepeatCount++;
        ActiveSequenceBuffer.Clear();
        return true;
    }

    private static bool TryStartRepeatedSequence(LogLevel level, string message)
    {
        var maxPatternLength = Math.Min(GetRepeatDetectionMaxLength(), RecentEntries.Count);
        for (var length = maxPatternLength; length >= 1; length--)
        {
            var startIndex = RecentEntries.Count - length;
            if (!RecentEntries[startIndex].Matches(level, message))
                continue;

            _activeSequence = new List<BufferedLogEntry>(length);
            for (var index = startIndex; index < RecentEntries.Count; index++)
            {
                _activeSequence.Add(RecentEntries[index]);
            }

            _activeSequenceRepeatCount = 0;
            _activeSequenceIndex = 0;
            ActiveSequenceBuffer.Clear();

            if (length == 1)
            {
                _activeSequenceRepeatCount = 1;
                return true;
            }

            _activeSequenceIndex = 1;
            ActiveSequenceBuffer.Add(new BufferedLogEntry(level, message));
            return true;
        }

        return false;
    }

    private static void FlushPendingState(ManualLogSource logger)
    {
        if (_activeSequence != null && _activeSequenceRepeatCount > 0)
        {
            Log(logger, GetSummaryLevel(), Format("Logger", BuildRepeatSummary()));
        }

        if (ActiveSequenceBuffer.Count > 0)
        {
            foreach (var entry in ActiveSequenceBuffer)
            {
                Log(logger, entry.Level, entry.Message);
                RememberEntry(entry);
            }
        }

        ActiveSequenceBuffer.Clear();
        _activeSequence = null;
        _activeSequenceIndex = 0;
        _activeSequenceRepeatCount = 0;
    }

    private static string BuildRepeatSummary()
    {
        if (_activeSequence == null || _activeSequence.Count == 0)
            return "Repeated log sequence suppressed";

        if (_activeSequence.Count == 1)
            return $"Previous message repeated {_activeSequenceRepeatCount} additional time(s)";

        return $"Previous {_activeSequence.Count}-message sequence repeated {_activeSequenceRepeatCount} additional time(s)";
    }

    private static LogLevel GetSummaryLevel()
    {
        if (_activeSequence == null || _activeSequence.Count == 0)
            return LogLevel.Info;

        return _activeSequence[0].Level;
    }

    private static int GetRepeatDetectionMaxLength() => 3;

    private static void RememberEntry(BufferedLogEntry entry)
    {
        RecentEntries.Add(entry);

        var maxPatternLength = GetRepeatDetectionMaxLength();
        while (RecentEntries.Count > maxPatternLength)
        {
            RecentEntries.RemoveAt(0);
        }
    }

    private static void Log(ManualLogSource logger, LogLevel level, string message)
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
