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

/// <summary>
/// Storm suppression in front of the sink. A warning or error whose event declares a storm key
/// is written once per key per <see cref="StormWindow"/>; repeats are counted and summarized as
/// <c>logging.storm.suppressed</c> when the window expires, the key is recovered or evicted, or
/// the pipeline flushes at shutdown. A warning keys on its named storm fields; an error keys on
/// its correlation fields and the exception type. Any key that cannot be built fails open.
/// </summary>
internal sealed class BppLogPipeline
{
    internal const int MaximumActiveStormKeys = 256;
    internal static readonly TimeSpan StormWindow = TimeSpan.FromSeconds(30);

    private static readonly BppLogEvent StormSuppressed = new(
        BppLogFeatureScope.Logger,
        "logging.storm.suppressed"
    );

    [ThreadStatic]
    private static bool _insideSink;

    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, StormEntry> _entries = new(StringComparer.Ordinal);
    private readonly Action<BppLogSeverity, string> _sink;
    private readonly object _stateLock = new();
    private bool _acceptStormState = true;
    private long _sequence;

    internal BppLogPipeline(Action<BppLogSeverity, string> sink, Func<DateTimeOffset> clock)
    {
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
        BppLogEvent logEvent,
        IReadOnlyList<BppLogField>? fields = null,
        Exception? exception = null
    )
    {
        if (_insideSink)
            return;

        try
        {
            if (!TryGetUtcNow(out var now))
            {
                WriteSink(severity, BppLogEventRenderer.Render(logEvent, fields, exception));
                return;
            }

            var hasStormKey = TryBuildStormKey(
                severity,
                logEvent,
                fields,
                exception,
                out var stormKey
            );
            List<PendingEmission>? pending;
            lock (_stateLock)
            {
                pending = ExpireEntries(now);
                if (!hasStormKey || !_acceptStormState)
                {
                    (pending ??= []).Add(
                        PendingEmission.Source(severity, logEvent, fields, exception)
                    );
                }
                else if (_entries.TryGetValue(stormKey, out var existing))
                {
                    existing.SuppressedCount++;
                    existing.LastTouchedSequence = NextSequence();
                }
                else
                {
                    if (_entries.Count >= MaximumActiveStormKeys)
                        EvictLeastRecentlyUsed(ref pending);

                    var entry = new StormEntry(
                        stormKey,
                        logEvent.Id,
                        severity,
                        now,
                        NextSequence()
                    );
                    _entries.Add(stormKey, entry);
                    (pending ??= []).Add(
                        PendingEmission.Source(severity, logEvent, fields, exception, entry)
                    );
                }
            }

            WritePending(pending);
        }
        catch
        {
            // Operational logging is best effort and must never alter game behavior.
        }
    }

    /// <summary>Ends every active storm of the event and writes its pending summaries.</summary>
    internal void RecoverStorm(BppLogEvent logEvent) =>
        RecoverStormCore(logEvent.Id, stormKey: null);

    /// <summary>Ends the warning storm whose key the given fields build.</summary>
    internal void RecoverStorm(BppLogEvent logEvent, IReadOnlyList<BppLogField>? fields)
    {
        if (
            !TryBuildStormKey(
                BppLogSeverity.Warning,
                logEvent,
                fields,
                exception: null,
                out var stormKey
            )
        )
            return;

        RecoverStormCore(logEvent.Id, stormKey);
    }

    private void RecoverStormCore(string eventId, string? stormKey)
    {
        if (_insideSink)
            return;

        try
        {
            if (!TryGetUtcNow(out var now))
                now = DateTimeOffset.MinValue;

            List<PendingEmission>? pending;
            lock (_stateLock)
            {
                pending = ExpireEntries(now);
                List<string>? keys = null;
                foreach (var pair in _entries)
                {
                    if (
                        string.Equals(pair.Value.EventId, eventId, StringComparison.Ordinal)
                        && (
                            stormKey == null
                            || string.Equals(pair.Key, stormKey, StringComparison.Ordinal)
                        )
                    )
                        (keys ??= []).Add(pair.Key);
                }
                for (var index = 0; index < (keys?.Count ?? 0); index++)
                    RemoveEntry(keys![index], BppLogStormFlushReason.Recovered, ref pending);
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
            List<PendingEmission>? pending = null;
            lock (_stateLock)
            {
                _acceptStormState = false;
                foreach (var pair in _entries)
                    AddSummary(pair.Value, BppLogStormFlushReason.Shutdown, ref pending);
                _entries.Clear();
            }
            WritePending(pending);
        }
        catch
        {
            // Shutdown must continue even if summary rendering fails.
        }
    }

    private static bool TryBuildStormKey(
        BppLogSeverity severity,
        BppLogEvent logEvent,
        IReadOnlyList<BppLogField>? fields,
        Exception? exception,
        out string key
    )
    {
        key = string.Empty;
        try
        {
            if (logEvent.Storm == null)
                return false;

            var builder = new StringBuilder();
            builder.Append((int)severity).Append('|').Append(logEvent.Id);
            switch (severity)
            {
                case BppLogSeverity.Warning:
                    if (!AppendWarningKey(builder, logEvent.Storm, fields))
                        return false;
                    break;
                case BppLogSeverity.Error:
                    if (!AppendErrorKey(builder, fields, exception))
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

    // A storm key names categorical fields. A key that names a missing or null field, or one
    // carrying any correlation policy, fails open: the record is written rather than merged.
    private static bool AppendWarningKey(
        StringBuilder builder,
        IReadOnlyList<string> keyNames,
        IReadOnlyList<BppLogField>? fields
    )
    {
        for (var index = 0; index < keyNames.Count; index++)
        {
            if (
                !TryFindField(keyNames[index], fields, out var field)
                || field.Policy != BppLogCorrelationPolicy.None
                || field.Value == null
            )
                return false;

            var value = BppLogValueFormatter.FormatScalar(field.Value);
            builder
                .Append('|')
                .Append(field.Name)
                .Append('=')
                .Append(value.Length)
                .Append(':')
                .Append(value);
        }
        return true;
    }

    private static bool AppendErrorKey(
        StringBuilder builder,
        IReadOnlyList<BppLogField>? fields,
        Exception? exception
    )
    {
        if (exception == null)
            return false;

        for (var index = 0; index < (fields?.Count ?? 0); index++)
        {
            var field = fields![index];
            if (field.Policy == BppLogCorrelationPolicy.None)
                continue;
            if (field.Value == null)
                return false;
            builder
                .Append('|')
                .Append(field.Name)
                .Append('=')
                .Append(
                    BppLogValueFormatter.Hash(BppLogValueFormatter.FormatScalar(field.Value), 32)
                );
        }

        builder.Append("|exception=").Append(BppLogEventRenderer.ExceptionType(exception));
        return true;
    }

    private List<PendingEmission>? ExpireEntries(DateTimeOffset now)
    {
        List<string>? expiredKeys = null;
        foreach (var pair in _entries)
        {
            if (now - pair.Value.StartedAt >= StormWindow)
                (expiredKeys ??= []).Add(pair.Key);
        }
        if (expiredKeys == null)
            return null;

        List<PendingEmission>? pending = null;
        for (var index = 0; index < expiredKeys.Count; index++)
            RemoveEntry(expiredKeys[index], BppLogStormFlushReason.Expired, ref pending);
        return pending;
    }

    private void EvictLeastRecentlyUsed(ref List<PendingEmission>? pending)
    {
        StormEntry? oldest = null;
        foreach (var pair in _entries)
        {
            if (oldest == null || pair.Value.LastTouchedSequence < oldest.LastTouchedSequence)
                oldest = pair.Value;
        }
        if (oldest != null)
            RemoveEntry(oldest.Key, BppLogStormFlushReason.Evicted, ref pending);
    }

    private void RemoveEntry(
        string key,
        BppLogStormFlushReason reason,
        ref List<PendingEmission>? pending
    )
    {
        if (!_entries.TryGetValue(key, out var entry))
            return;
        _entries.Remove(key);
        AddSummary(entry, reason, ref pending);
    }

    private static void AddSummary(
        StormEntry entry,
        BppLogStormFlushReason reason,
        ref List<PendingEmission>? pending
    )
    {
        if (entry.SuppressedCount <= 0)
            return;
        (pending ??= []).Add(PendingEmission.Summary(entry, reason));
    }

    private void WritePending(IReadOnlyList<PendingEmission>? pending)
    {
        if (pending == null)
            return;

        for (var index = 0; index < pending.Count; index++)
        {
            var emission = pending[index];
            string message;
            if (emission.SummaryReason.HasValue)
            {
                if (!WaitForSourceDelivery(emission.Entry!))
                    continue;
                message = RenderSummary(emission.Entry!, emission.SummaryReason.Value);
            }
            else
            {
                message = BppLogEventRenderer.Render(
                    emission.Event,
                    emission.Fields,
                    emission.Exception
                );
            }

            var delivered = WriteSink(emission.Severity, message);
            if (!emission.SummaryReason.HasValue && emission.Entry != null)
                CompleteSourceDelivery(emission.Entry, delivered);
        }
    }

    private static string RenderSummary(StormEntry entry, BppLogStormFlushReason reason) =>
        BppLogEventRenderer.Render(
            StormSuppressed,
            new BppLogField[]
            {
                ("source_event", entry.EventId),
                ("suppressed_count", entry.SuppressedCount),
                ("window_ms", (long)StormWindow.TotalMilliseconds),
                ("flush_reason", reason),
            },
            exception: null
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

    private static bool TryFindField(
        string name,
        IReadOnlyList<BppLogField>? fields,
        out BppLogField field
    )
    {
        for (var index = 0; index < (fields?.Count ?? 0); index++)
        {
            if (!string.Equals(fields![index].Name, name, StringComparison.Ordinal))
                continue;
            field = fields[index];
            return true;
        }
        field = default;
        return false;
    }

    private sealed class StormEntry
    {
        internal StormEntry(
            string key,
            string eventId,
            BppLogSeverity severity,
            DateTimeOffset startedAt,
            long lastTouchedSequence
        )
        {
            Key = key;
            EventId = eventId;
            Severity = severity;
            StartedAt = startedAt;
            LastTouchedSequence = lastTouchedSequence;
        }

        internal string Key { get; }

        internal string EventId { get; }

        internal BppLogSeverity Severity { get; }

        internal DateTimeOffset StartedAt { get; }

        internal int SuppressedCount { get; set; }

        internal long LastTouchedSequence { get; set; }

        internal TaskCompletionSource<bool> SourceDelivery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Rendering is deferred to WritePending so a record discarded by the storm-key decision is
    // never rendered at all.
    private readonly struct PendingEmission
    {
        private PendingEmission(
            BppLogSeverity severity,
            BppLogEvent logEvent,
            IReadOnlyList<BppLogField>? fields,
            Exception? exception,
            StormEntry? entry,
            BppLogStormFlushReason? summaryReason
        )
        {
            Severity = severity;
            Event = logEvent;
            Fields = fields;
            Exception = exception;
            Entry = entry;
            SummaryReason = summaryReason;
        }

        internal BppLogSeverity Severity { get; }

        internal BppLogEvent Event { get; }

        internal IReadOnlyList<BppLogField>? Fields { get; }

        internal Exception? Exception { get; }

        internal StormEntry? Entry { get; }

        internal BppLogStormFlushReason? SummaryReason { get; }

        internal static PendingEmission Source(
            BppLogSeverity severity,
            BppLogEvent logEvent,
            IReadOnlyList<BppLogField>? fields,
            Exception? exception,
            StormEntry? entry = null
        ) => new(severity, logEvent, fields, exception, entry, null);

        internal static PendingEmission Summary(StormEntry entry, BppLogStormFlushReason reason) =>
            new(entry.Severity, default, null, null, entry, reason);
    }
}

internal enum BppLogStormFlushReason
{
    Expired,
    Recovered,
    Evicted,
    Shutdown,
}
