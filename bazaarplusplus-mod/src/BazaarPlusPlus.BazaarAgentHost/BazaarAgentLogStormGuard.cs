#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.BazaarAgent;

namespace BazaarPlusPlus.BazaarAgentHost;

internal sealed class BazaarAgentLogStormGuard
{
    [ThreadStatic]
    private static bool _insideSink;

    internal const int MaximumActiveKeys = 256;
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    private const int FingerprintInputCharacterBudget = 1024 * 1024;

    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, StormEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Func<BazaarAgentLogEvent, string> _renderer;
    private readonly Func<BazaarAgentLogSeverity, string, bool> _sink;
    private bool _acceptState = true;
    private int _deferredFlush;
    private int _flushInProgress;
    private long _sequence;

    internal BazaarAgentLogStormGuard(
        Func<BazaarAgentLogSeverity, string, bool> sink,
        Func<BazaarAgentLogEvent, string> renderer,
        Func<DateTimeOffset>? clock = null
    )
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal int ActiveKeyCount
    {
        get
        {
            try
            {
                lock (_gate)
                    return _entries.Count;
            }
            catch
            {
                return 0;
            }
        }
    }

    internal void Emit(BazaarAgentLogEvent logEvent, string rendered)
    {
        if (_insideSink)
            return;
        try
        {
            if (!TryGetNow(out var now))
            {
                WritePending(
                    new[] { PendingEmission.Source(logEvent.Definition.Severity, rendered) }
                );
                return;
            }
            var hasStormKey = TryBuildKey(logEvent, out var key);

            List<PendingEmission> pending;
            lock (_gate)
            {
                pending = Expire(now);
                Recover(logEvent.Definition.RecoversEventId, pending);
                if (!hasStormKey || !_acceptState)
                {
                    pending.Add(PendingEmission.Source(logEvent.Definition.Severity, rendered));
                }
                else if (_entries.TryGetValue(key, out var existing))
                {
                    existing.SuppressedCount++;
                    existing.LastTouchedSequence = NextSequence();
                }
                else
                {
                    if (_entries.Count >= MaximumActiveKeys)
                        EvictLeastRecentlyUsed(pending);
                    var entry = new StormEntry(
                        key,
                        logEvent.Definition.EventId,
                        now,
                        NextSequence()
                    );
                    _entries.Add(key, entry);
                    pending.Add(
                        PendingEmission.Source(logEvent.Definition.Severity, rendered, entry)
                    );
                }
            }

            WritePending(pending);
        }
        catch
        {
            // Storm control is a safety net and must never affect Agent behavior.
        }
    }

    internal void Flush()
    {
        if (_insideSink)
        {
            if (Volatile.Read(ref _flushInProgress) == 0)
                Interlocked.Exchange(ref _deferredFlush, 1);
            return;
        }
        if (Interlocked.Exchange(ref _flushInProgress, 1) == 1)
            return;
        try
        {
            var pending = new List<PendingEmission>();
            lock (_gate)
            {
                _acceptState = false;
                foreach (var entry in _entries.Values)
                    AddSummary(entry, BazaarAgentLogStormFlushReason.Shutdown, pending);
                _entries.Clear();
            }
            WritePending(pending);
        }
        catch
        {
            // Host shutdown must continue even when logging fails.
        }
        finally
        {
            Volatile.Write(ref _flushInProgress, 0);
        }
    }

    private bool TryBuildKey(BazaarAgentLogEvent logEvent, out string key)
    {
        key = string.Empty;
        try
        {
            var definition = logEvent.Definition;
            var policy = definition.StormPolicy;
            if (policy == null)
                return false;
            if (
                definition.Severity != BazaarAgentLogSeverity.Warning
                && definition.Severity != BazaarAgentLogSeverity.Error
            )
                return false;

            var builder = new StringBuilder(definition.EventId);
            foreach (var field in policy.KeyFields)
            {
                if (
                    !ContainsField(definition, field)
                    || !TryFindValue(logEvent.Values, field, out var value)
                    || !TryFingerprintValue(value, out var fingerprint)
                )
                    return false;
                if (
                    definition.Severity == BazaarAgentLogSeverity.Warning
                    && (
                        field.Cardinality != BazaarAgentLogCardinality.Low
                        || field.Correlation != BazaarAgentLogCorrelation.None
                    )
                )
                    return false;
                builder.Append('|').Append(field.Name).Append('=').Append(fingerprint);
            }

            if (definition.Severity == BazaarAgentLogSeverity.Error)
            {
                if (!TryFingerprintException(logEvent.Exception, out var exceptionFingerprint))
                    return false;
                builder.Append("|exception=").Append(exceptionFingerprint);
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

    private static bool TryFingerprintValue(object? value, out string fingerprint)
    {
        fingerprint = string.Empty;
        try
        {
            var raw = value switch
            {
                null => "null",
                string text => text,
                char character => character.ToString(),
                bool boolean => boolean ? "true" : "false",
                Enum enumValue => enumValue.ToString(),
                DateTime timestamp => timestamp
                    .ToUniversalTime()
                    .ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset timestamp => timestamp.UtcDateTime.ToString(
                    "O",
                    CultureInfo.InvariantCulture
                ),
                Guid guid => guid.ToString("D"),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)
                    ?? "null",
                _ => value.ToString() ?? "null",
            };
            return TryHash(raw, out fingerprint);
        }
        catch
        {
            fingerprint = string.Empty;
            return false;
        }
    }

    private static bool TryFingerprintException(Exception? exception, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (exception == null)
            return false;

        try
        {
            var canonical = new StringBuilder("exception-v1;");
            var current = exception;
            var depth = 0;
            for (; depth < 4 && current != null; depth++)
            {
                if (current is AggregateException aggregate && aggregate.InnerExceptions.Count > 1)
                    return false;
                if (
                    !TryHash(current.GetType().FullName ?? current.GetType().Name, out var type)
                    || !TryHash(current.Message ?? string.Empty, out var message)
                    || !TryHash(current.StackTrace ?? string.Empty, out var stack)
                )
                    return false;
                canonical
                    .Append(depth)
                    .Append(':')
                    .Append(type)
                    .Append(':')
                    .Append(
                        unchecked((uint)current.HResult).ToString(
                            "X8",
                            CultureInfo.InvariantCulture
                        )
                    )
                    .Append(':')
                    .Append(message)
                    .Append(':')
                    .Append(stack)
                    .Append(';');
                current = current.InnerException;
            }
            if (current != null)
                return false;
            return TryHash(canonical.ToString(), out fingerprint);
        }
        catch
        {
            fingerprint = string.Empty;
            return false;
        }
    }

    private static bool TryHash(string value, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (value.Length > FingerprintInputCharacterBudget)
            return false;
        try
        {
            var bytes = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true
            ).GetBytes(value);
            using var sha256 = SHA256.Create();
            var digest = sha256.ComputeHash(bytes);
            var builder = new StringBuilder(digest.Length * 2);
            for (var index = 0; index < digest.Length; index++)
                builder.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
            fingerprint = builder.ToString();
            return true;
        }
        catch
        {
            fingerprint = string.Empty;
            return false;
        }
    }

    private List<PendingEmission> Expire(DateTimeOffset now)
    {
        var pending = new List<PendingEmission>();
        var expired = new List<string>();
        foreach (var pair in _entries)
        {
            if (now - pair.Value.StartedAt >= Window)
                expired.Add(pair.Key);
        }
        foreach (var key in expired)
            RemoveEntry(key, BazaarAgentLogStormFlushReason.Expired, pending);
        return pending;
    }

    private void Recover(string? sourceEvent, List<PendingEmission> pending)
    {
        if (string.IsNullOrEmpty(sourceEvent))
            return;
        var matching = new List<string>();
        foreach (var pair in _entries)
        {
            if (string.Equals(pair.Value.SourceEvent, sourceEvent, StringComparison.Ordinal))
                matching.Add(pair.Key);
        }
        foreach (var key in matching)
            RemoveEntry(key, BazaarAgentLogStormFlushReason.Recovered, pending);
    }

    private void EvictLeastRecentlyUsed(List<PendingEmission> pending)
    {
        StormEntry? oldest = null;
        foreach (var entry in _entries.Values)
        {
            if (oldest == null || entry.LastTouchedSequence < oldest.LastTouchedSequence)
                oldest = entry;
        }
        if (oldest != null)
            RemoveEntry(oldest.Key, BazaarAgentLogStormFlushReason.Evicted, pending);
    }

    private void RemoveEntry(
        string key,
        BazaarAgentLogStormFlushReason reason,
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
        BazaarAgentLogStormFlushReason reason,
        List<PendingEmission> pending
    )
    {
        if (entry.SuppressedCount <= 0)
            return;
        pending.Add(
            PendingEmission.Summary(
                BazaarAgentLogRuntimeEvents.StormSuppressed(
                    entry.SourceEvent,
                    entry.SuppressedCount,
                    (long)Window.TotalMilliseconds,
                    reason
                ),
                entry
            )
        );
    }

    private void WritePending(IReadOnlyList<PendingEmission> pending)
    {
        foreach (var emission in pending)
        {
            var message = emission.Message;
            if (message == null && emission.SummaryEvent != null)
            {
                if (
                    emission.SummarySourceEntry == null
                    || !WaitForSourceDelivery(emission.SummarySourceEntry)
                )
                    continue;
                message = _renderer(emission.SummaryEvent);
            }
            if (message == null)
                continue;
            var delivered = false;
            try
            {
                _insideSink = true;
                delivered = _sink(emission.Severity, message);
            }
            catch
            {
                delivered = false;
            }
            finally
            {
                _insideSink = false;
                if (emission.SourceEntry != null)
                    CompleteSourceDelivery(emission.SourceEntry, delivered);
            }
        }
        if (Interlocked.Exchange(ref _deferredFlush, 0) == 1)
            Flush();
    }

    private void CompleteSourceDelivery(StormEntry entry, bool delivered)
    {
        try
        {
            if (!delivered)
            {
                lock (_gate)
                {
                    if (
                        _entries.TryGetValue(entry.Key, out var current)
                        && ReferenceEquals(current, entry)
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

    private bool TryGetNow(out DateTimeOffset now)
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

    private long NextSequence() => ++_sequence;

    private static bool ContainsField(
        BazaarAgentLogEventDefinition definition,
        BazaarAgentLogFieldDefinition field
    )
    {
        foreach (var declared in definition.Fields)
        {
            if (ReferenceEquals(declared, field))
                return true;
        }
        return false;
    }

    private static bool TryFindValue(
        IReadOnlyList<BazaarAgentLogFieldValue> values,
        BazaarAgentLogFieldDefinition field,
        out object? value
    )
    {
        foreach (var candidate in values)
        {
            if (!ReferenceEquals(candidate.Field, field))
                continue;
            value = candidate.Value;
            return true;
        }
        value = null;
        return false;
    }

    private sealed class StormEntry
    {
        internal StormEntry(string key, string sourceEvent, DateTimeOffset startedAt, long sequence)
        {
            Key = key;
            SourceEvent = sourceEvent;
            StartedAt = startedAt;
            LastTouchedSequence = sequence;
        }

        internal string Key { get; }
        internal string SourceEvent { get; }
        internal DateTimeOffset StartedAt { get; }
        internal int SuppressedCount { get; set; }
        internal long LastTouchedSequence { get; set; }
        internal TaskCompletionSource<bool> SourceDelivery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly struct PendingEmission
    {
        private PendingEmission(
            BazaarAgentLogSeverity severity,
            string? message,
            StormEntry? sourceEntry,
            BazaarAgentLogEvent? summaryEvent,
            StormEntry? summarySourceEntry
        )
        {
            Severity = severity;
            Message = message;
            SourceEntry = sourceEntry;
            SummaryEvent = summaryEvent;
            SummarySourceEntry = summarySourceEntry;
        }

        internal BazaarAgentLogSeverity Severity { get; }
        internal string? Message { get; }
        internal StormEntry? SourceEntry { get; }
        internal BazaarAgentLogEvent? SummaryEvent { get; }
        internal StormEntry? SummarySourceEntry { get; }

        internal static PendingEmission Source(
            BazaarAgentLogSeverity severity,
            string message,
            StormEntry? entry = null
        ) => new(severity, message, entry, summaryEvent: null, summarySourceEntry: null);

        internal static PendingEmission Summary(
            BazaarAgentLogEvent logEvent,
            StormEntry sourceEntry
        ) =>
            new(
                logEvent.Definition.Severity,
                message: null,
                sourceEntry: null,
                summaryEvent: logEvent,
                summarySourceEntry: sourceEntry
            );
    }
}
