#nullable enable
using System.Text;

namespace BazaarPlusPlus.Infrastructure.Logging;

internal enum BppLogSeverity
{
    Debug,
    Info,
    Warning,
    Error,
}

internal sealed class BppLogPipeline
{
    internal const int MaximumActiveStormKeys = 256;
    internal static readonly TimeSpan StormWindow = TimeSpan.FromSeconds(30);

    [ThreadStatic]
    private static bool _insideSink;

    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, StormEntry> _entries = new(StringComparer.Ordinal);
    private readonly BppLogEventRenderer _renderer;
    private readonly Action<BppLogSeverity, string> _sink;
    private readonly object _stateLock = new();
    private bool _acceptStormState = true;
    private long _sequence;

    internal BppLogPipeline(
        BppLogEventRenderer renderer,
        Action<BppLogSeverity, string> sink,
        Func<DateTimeOffset> clock
    )
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    internal int ActiveStormKeyCount
    {
        get
        {
            try
            {
                lock (_stateLock)
                    return _entries.Count;
            }
            catch
            {
                return 0;
            }
        }
    }

    internal void Emit(
        BppLogSeverity severity,
        BppLogEventDefinition definition,
        IReadOnlyList<BppLogFieldValue>? values = null,
        Exception? exception = null
    )
    {
        if (_insideSink)
            return;

        try
        {
            var rendered = _renderer.Render(definition, values, exception);
            if (!TryGetUtcNow(out var now))
            {
                WriteSink(severity, rendered);
                return;
            }

            var hasStormKey = TryBuildStormKey(
                severity,
                definition,
                values,
                exception,
                out var stormKey
            );
            List<PendingEmission> pending;
            lock (_stateLock)
            {
                pending = ExpireEntries(now);
                if (!hasStormKey || !_acceptStormState)
                {
                    pending.Add(PendingEmission.Source(severity, rendered));
                }
                else if (_entries.TryGetValue(stormKey, out var existing))
                {
                    existing.SuppressedCount++;
                    existing.LastTouchedSequence = NextSequence();
                }
                else
                {
                    if (_entries.Count >= MaximumActiveStormKeys)
                        EvictLeastRecentlyUsed(pending);

                    var entry = new StormEntry(stormKey, definition, severity, now, NextSequence());
                    _entries.Add(stormKey, entry);
                    pending.Add(PendingEmission.Source(severity, rendered, entry));
                }
            }

            WritePending(pending);
        }
        catch
        {
            // Operational logging is best effort and must never alter game behavior.
        }
    }

    internal void RecoverStorm(BppLogEventDefinition definition)
    {
        RecoverStormCore(definition, stormKey: null);
    }

    internal void RecoverStorm(
        BppLogEventDefinition definition,
        IReadOnlyList<BppLogFieldValue>? values
    )
    {
        if (
            !TryBuildStormKey(
                BppLogSeverity.Warning,
                definition,
                values,
                exception: null,
                out var stormKey
            )
        )
            return;

        RecoverStormCore(definition, stormKey);
    }

    private void RecoverStormCore(BppLogEventDefinition definition, string? stormKey)
    {
        if (_insideSink)
            return;

        try
        {
            if (!TryGetUtcNow(out var now))
                now = DateTimeOffset.MinValue;

            List<PendingEmission> pending;
            lock (_stateLock)
            {
                pending = ExpireEntries(now);
                var keys = new List<string>();
                foreach (var pair in _entries)
                {
                    if (
                        ReferenceEquals(pair.Value.Definition, definition)
                        && (
                            stormKey == null
                            || string.Equals(pair.Key, stormKey, StringComparison.Ordinal)
                        )
                    )
                        keys.Add(pair.Key);
                }
                for (var index = 0; index < keys.Count; index++)
                    RemoveEntry(keys[index], BppLogStormFlushReason.Recovered, pending);
            }
            WritePending(pending);
        }
        catch
        {
            // Recovery reporting is best effort.
        }
    }

    internal void Flush()
    {
        if (_insideSink)
            return;

        try
        {
            var pending = new List<PendingEmission>();
            lock (_stateLock)
            {
                _acceptStormState = false;
                foreach (var pair in _entries)
                    AddSummary(pair.Value, BppLogStormFlushReason.Shutdown, pending);
                _entries.Clear();
            }
            WritePending(pending);
        }
        catch
        {
            // Shutdown must continue even if summary rendering fails.
        }
    }

    private bool TryBuildStormKey(
        BppLogSeverity severity,
        BppLogEventDefinition definition,
        IReadOnlyList<BppLogFieldValue>? values,
        Exception? exception,
        out string key
    )
    {
        key = string.Empty;
        try
        {
            if (definition == null || definition.StormPolicy == null)
                return false;

            var builder = new StringBuilder();
            builder.Append((int)severity).Append('|').Append(definition.EventId);
            switch (severity)
            {
                case BppLogSeverity.Warning:
                    if (!AppendWarningKey(builder, definition, values))
                        return false;
                    break;
                case BppLogSeverity.Error:
                    if (!AppendErrorKey(builder, definition, values, exception))
                        return false;
                    break;
                default:
                    return false;
            }

            key = builder.ToString();
            return true;
        }
        catch
        {
            key = string.Empty;
            return false;
        }
    }

    private bool AppendWarningKey(
        StringBuilder builder,
        BppLogEventDefinition definition,
        IReadOnlyList<BppLogFieldValue>? values
    )
    {
        var keyFields = definition.StormPolicy?.KeyFields;
        if (keyFields == null)
            return false;

        for (var index = 0; index < keyFields.Count; index++)
        {
            var field = keyFields[index];
            if (
                field == null
                || !ContainsField(definition, field)
                || field.Cardinality != BppLogCardinality.Low
                || field.Correlation != BppLogCorrelationPolicy.None
                || field.Privacy == BppLogFieldPrivacy.Sensitive
                || !TryFindValue(field, values, out var value)
                || !_renderer.TryFingerprint(field, value, out var fingerprint)
            )
                return false;

            builder.Append('|').Append(field.Order).Append('=').Append(fingerprint);
        }
        return true;
    }

    private bool AppendErrorKey(
        StringBuilder builder,
        BppLogEventDefinition definition,
        IReadOnlyList<BppLogFieldValue>? values,
        Exception? exception
    )
    {
        var correlationFields = new List<BppLogFieldDefinition>();
        for (var index = 0; index < definition.Fields.Count; index++)
        {
            var field = definition.Fields[index];
            if (field != null && field.Correlation != BppLogCorrelationPolicy.None)
                correlationFields.Add(field);
        }
        correlationFields.Sort((left, right) => left.Order.CompareTo(right.Order));

        for (var index = 0; index < correlationFields.Count; index++)
        {
            var field = correlationFields[index];
            if (
                !TryFindValue(field, values, out var value)
                || !_renderer.TryFingerprint(field, value, out var fingerprint)
            )
                return false;
            builder.Append('|').Append(field.Order).Append('=').Append(fingerprint);
        }

        if (!_renderer.TryFingerprint(exception, out var exceptionFingerprint))
            return false;
        builder.Append("|exception=").Append(exceptionFingerprint);
        return true;
    }

    private List<PendingEmission> ExpireEntries(DateTimeOffset now)
    {
        var pending = new List<PendingEmission>();
        var expiredKeys = new List<string>();
        foreach (var pair in _entries)
        {
            if (now - pair.Value.StartedAt >= StormWindow)
                expiredKeys.Add(pair.Key);
        }
        for (var index = 0; index < expiredKeys.Count; index++)
            RemoveEntry(expiredKeys[index], BppLogStormFlushReason.Expired, pending);
        return pending;
    }

    private void EvictLeastRecentlyUsed(List<PendingEmission> pending)
    {
        StormEntry? oldest = null;
        foreach (var pair in _entries)
        {
            if (oldest == null || pair.Value.LastTouchedSequence < oldest.LastTouchedSequence)
                oldest = pair.Value;
        }
        if (oldest != null)
            RemoveEntry(oldest.Key, BppLogStormFlushReason.Evicted, pending);
    }

    private void RemoveEntry(
        string key,
        BppLogStormFlushReason reason,
        List<PendingEmission> pending
    )
    {
        if (!_entries.TryGetValue(key, out var entry))
            return;
        _entries.Remove(key);
        AddSummary(entry, reason, pending);
    }

    private static void AddSummary(
        StormEntry entry,
        BppLogStormFlushReason reason,
        List<PendingEmission> pending
    )
    {
        if (entry.SuppressedCount <= 0)
            return;
        pending.Add(
            PendingEmission.Summary(
                entry.Severity,
                entry.Definition.EventId,
                entry.SuppressedCount,
                reason,
                entry
            )
        );
    }

    private void WritePending(IReadOnlyList<PendingEmission> pending)
    {
        for (var index = 0; index < pending.Count; index++)
        {
            var emission = pending[index];
            var message = emission.Message;
            if (message == null && emission.StormSummary.HasValue)
            {
                var summary = emission.StormSummary.Value;
                if (!WaitForSourceDelivery(summary.Entry))
                    continue;
                message = RenderSummary(summary);
            }
            if (message != null)
            {
                var delivered = WriteSink(emission.Severity, message);
                if (emission.SourceEntry != null)
                    CompleteSourceDelivery(emission.SourceEntry, delivered);
            }
        }
    }

    private string RenderSummary(StormSummary summary) =>
        _renderer.Render(
            BppLogRuntimeEvents.StormSuppressed,
            new[]
            {
                BppLogRuntimeEvents.SourceEvent.Bind(summary.SourceEvent),
                BppLogRuntimeEvents.SuppressedCount.Bind(summary.SuppressedCount),
                BppLogRuntimeEvents.WindowMilliseconds.Bind((long)StormWindow.TotalMilliseconds),
                BppLogRuntimeEvents.FlushReason.Bind(summary.Reason),
            }
        );

    private bool WriteSink(BppLogSeverity severity, string message)
    {
        if (_insideSink)
            return false;

        try
        {
            _insideSink = true;
            _sink(severity, message);
            return true;
        }
        catch
        {
            // Never recursively report sink failures.
            return false;
        }
        finally
        {
            _insideSink = false;
        }
    }

    private void CompleteSourceDelivery(StormEntry entry, bool delivered)
    {
        try
        {
            if (!delivered)
            {
                lock (_stateLock)
                {
                    if (
                        _entries.TryGetValue(entry.Key, out var active)
                        && ReferenceEquals(active, entry)
                    )
                        _entries.Remove(entry.Key);
                }
            }
        }
        catch
        {
            delivered = false;
        }
        finally
        {
            entry.SourceDelivery.TrySetResult(delivered);
        }
    }

    private static bool WaitForSourceDelivery(StormEntry entry)
    {
        try
        {
            return entry.SourceDelivery.Task.GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetUtcNow(out DateTimeOffset now)
    {
        try
        {
            now = _clock();
            return true;
        }
        catch
        {
            now = default;
            return false;
        }
    }

    private long NextSequence() => unchecked(++_sequence);

    private static bool ContainsField(BppLogEventDefinition definition, BppLogFieldDefinition field)
    {
        for (var index = 0; index < definition.Fields.Count; index++)
        {
            if (ReferenceEquals(definition.Fields[index], field))
                return true;
        }
        return false;
    }

    private static bool TryFindValue(
        BppLogFieldDefinition field,
        IReadOnlyList<BppLogFieldValue>? values,
        out object? value
    )
    {
        if (values != null)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (!ReferenceEquals(values[index].Field, field))
                    continue;
                value = values[index].Value;
                return value != null;
            }
        }
        value = null;
        return false;
    }

    private sealed class StormEntry
    {
        internal StormEntry(
            string key,
            BppLogEventDefinition definition,
            BppLogSeverity severity,
            DateTimeOffset startedAt,
            long lastTouchedSequence
        )
        {
            Key = key;
            Definition = definition;
            Severity = severity;
            StartedAt = startedAt;
            LastTouchedSequence = lastTouchedSequence;
        }

        internal string Key { get; }

        internal BppLogEventDefinition Definition { get; }

        internal BppLogSeverity Severity { get; }

        internal DateTimeOffset StartedAt { get; }

        internal int SuppressedCount { get; set; }

        internal long LastTouchedSequence { get; set; }

        internal TaskCompletionSource<bool> SourceDelivery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly struct PendingEmission
    {
        private PendingEmission(
            BppLogSeverity severity,
            string? message,
            StormSummary? stormSummary,
            StormEntry? sourceEntry
        )
        {
            Severity = severity;
            Message = message;
            StormSummary = stormSummary;
            SourceEntry = sourceEntry;
        }

        internal BppLogSeverity Severity { get; }

        internal string? Message { get; }

        internal StormSummary? StormSummary { get; }

        internal StormEntry? SourceEntry { get; }

        internal static PendingEmission Source(
            BppLogSeverity severity,
            string message,
            StormEntry? entry = null
        ) => new(severity, message, null, entry);

        internal static PendingEmission Summary(
            BppLogSeverity severity,
            string sourceEvent,
            int suppressedCount,
            BppLogStormFlushReason reason,
            StormEntry entry
        ) =>
            new(
                severity,
                null,
                new StormSummary(sourceEvent, suppressedCount, reason, entry),
                null
            );
    }

    private readonly struct StormSummary
    {
        internal StormSummary(
            string sourceEvent,
            int suppressedCount,
            BppLogStormFlushReason reason,
            StormEntry entry
        )
        {
            SourceEvent = sourceEvent;
            SuppressedCount = suppressedCount;
            Reason = reason;
            Entry = entry;
        }

        internal string SourceEvent { get; }

        internal int SuppressedCount { get; }

        internal BppLogStormFlushReason Reason { get; }

        internal StormEntry Entry { get; }
    }
}

internal enum BppLogStormFlushReason
{
    Expired,
    Recovered,
    Evicted,
    Shutdown,
}

[BppLogEventSource]
internal static class BppLogRuntimeEvents
{
    internal static readonly BppLogFieldDefinition SourceEvent = new(
        0,
        "source_event",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition SuppressedCount = new(
        1,
        "suppressed_count",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.High
    );
    internal static readonly BppLogFieldDefinition WindowMilliseconds = new(
        2,
        "window_ms",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );
    internal static readonly BppLogFieldDefinition FlushReason = new(
        3,
        "flush_reason",
        BppLogFieldPrivacy.Public,
        BppLogCorrelationPolicy.None,
        BppLogCardinality.Low
    );
    internal static readonly BppLogEventDefinition StormSuppressed = new(
        BppLogFeatureScope.Logger,
        "logging.storm.suppressed",
        new[] { SourceEvent, SuppressedCount, WindowMilliseconds, FlushReason }
    );
}
