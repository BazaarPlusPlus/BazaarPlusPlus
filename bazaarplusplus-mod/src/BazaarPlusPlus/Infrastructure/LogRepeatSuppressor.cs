#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Infrastructure;

internal sealed class LogRepeatSuppressor
{
    private readonly object _syncRoot = new object();
    private readonly List<Entry> _recent = new List<Entry>();
    private readonly List<Entry> _activeBuffer = new List<Entry>();
    private readonly Action<int, string> _writeSink;
    private readonly Func<string, int, string> _formatSummary;
    private readonly int _maxPatternLength;

    private List<Entry>? _activeSequence;
    private int _activeSequenceIndex;
    private int _activeSequenceRepeatCount;

    public LogRepeatSuppressor(
        Action<int, string> writeSink,
        Func<string, int, string> formatSummary,
        int maxPatternLength = 3
    )
    {
        if (maxPatternLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPatternLength));

        _writeSink = writeSink ?? throw new ArgumentNullException(nameof(writeSink));
        _formatSummary = formatSummary ?? throw new ArgumentNullException(nameof(formatSummary));
        _maxPatternLength = maxPatternLength;
    }

    public void Write(int level, string message)
    {
        lock (_syncRoot)
        {
            var flushedActiveSequence = false;
            if (TryConsumeActiveSequence(level, message, ref flushedActiveSequence))
                return;

            if (flushedActiveSequence)
            {
                _writeSink(level, message);
                Remember(new Entry(level, message));
                return;
            }

            if (TryStartRepeatedSequence(level, message))
                return;

            _writeSink(level, message);
            Remember(new Entry(level, message));
        }
    }

    public void Flush()
    {
        lock (_syncRoot)
        {
            FlushPendingState();
            _recent.Clear();
        }
    }

    private readonly struct Entry
    {
        public Entry(int level, string message)
        {
            Level = level;
            Message = message;
        }

        public int Level { get; }
        public string Message { get; }

        public bool Matches(int level, string message) =>
            Level == level && string.Equals(Message, message, StringComparison.Ordinal);
    }

    private bool TryConsumeActiveSequence(int level, string message, ref bool flushedActiveSequence)
    {
        if (_activeSequence == null || _activeSequence.Count == 0)
            return false;

        var expected = _activeSequence[_activeSequenceIndex];
        if (!expected.Matches(level, message))
        {
            FlushPendingState();
            flushedActiveSequence = true;
            return false;
        }

        if (_activeSequence.Count > 1)
            _activeBuffer.Add(new Entry(level, message));

        _activeSequenceIndex++;
        if (_activeSequenceIndex < _activeSequence.Count)
            return true;

        _activeSequenceIndex = 0;
        _activeSequenceRepeatCount++;
        _activeBuffer.Clear();
        return true;
    }

    private bool TryStartRepeatedSequence(int level, string message)
    {
        var maxLen = Math.Min(_maxPatternLength, _recent.Count);
        for (var length = maxLen; length >= 1; length--)
        {
            var start = _recent.Count - length;
            if (!_recent[start].Matches(level, message))
                continue;

            _activeSequence = new List<Entry>(length);
            for (var i = start; i < _recent.Count; i++)
                _activeSequence.Add(_recent[i]);

            _activeSequenceRepeatCount = 0;
            _activeSequenceIndex = 0;
            _activeBuffer.Clear();

            if (length == 1)
            {
                _activeSequenceRepeatCount = 1;
                return true;
            }

            _activeSequenceIndex = 1;
            _activeBuffer.Add(new Entry(level, message));
            return true;
        }

        return false;
    }

    private void FlushPendingState()
    {
        if (_activeSequence != null && _activeSequenceRepeatCount > 0)
        {
            var summaryLevel = _activeSequence[0].Level;
            var summaryText = BuildRepeatSummary();
            _writeSink(summaryLevel, _formatSummary(summaryText, summaryLevel));
        }

        if (_activeBuffer.Count > 0)
        {
            foreach (var entry in _activeBuffer)
            {
                _writeSink(entry.Level, entry.Message);
                Remember(entry);
            }
        }

        _activeBuffer.Clear();
        _activeSequence = null;
        _activeSequenceIndex = 0;
        _activeSequenceRepeatCount = 0;
    }

    private string BuildRepeatSummary()
    {
        if (_activeSequence == null || _activeSequence.Count == 0)
            return "Repeated log sequence suppressed";

        if (_activeSequence.Count == 1)
            return $"Previous message repeated {_activeSequenceRepeatCount} additional time(s)";

        return $"Previous {_activeSequence.Count}-message sequence repeated {_activeSequenceRepeatCount} additional time(s)";
    }

    private void Remember(Entry entry)
    {
        _recent.Add(entry);
        while (_recent.Count > _maxPatternLength)
            _recent.RemoveAt(0);
    }
}
