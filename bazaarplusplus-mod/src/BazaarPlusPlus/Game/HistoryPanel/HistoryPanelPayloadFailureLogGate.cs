#nullable enable
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelPayloadFailureLogGate
{
    private const int MaximumEntries = 256;
    private readonly Dictionary<string, PayloadFailureEntry> _entries = new(StringComparer.Ordinal);
    private long _sequence;

    internal int Count => _entries.Count;

    internal void Report(
        string battleId,
        string fingerprint,
        HistoryPanelPreviewPayloadReasonCode reasonCode,
        Exception? exception
    )
    {
        if (
            _entries.TryGetValue(battleId, out var previous)
            && string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal)
            && EqualityComparer<HistoryPanelPreviewPayloadReasonCode>.Default.Equals(
                previous.ReasonCode,
                reasonCode
            )
        )
        {
            _entries[battleId] = new PayloadFailureEntry(fingerprint, reasonCode, NextSequence());
            return;
        }

        if (!_entries.ContainsKey(battleId) && _entries.Count >= MaximumEntries)
            EvictLeastRecentlyUsed();

        var reasonField = HistoryPanelLogEvents.PreviewPayloadReasonCode.Bind(reasonCode);
        BppLog.RecoverStorm(HistoryPanelLogEvents.PreviewPayloadDegraded, reasonField);
        var fields = new[]
        {
            HistoryPanelLogEvents.PreviewPayloadBattleId.Bind(battleId),
            reasonField,
        };
        if (exception == null)
            BppLog.WarnEvent(HistoryPanelLogEvents.PreviewPayloadDegraded, fields);
        else
            BppLog.WarnEvent(HistoryPanelLogEvents.PreviewPayloadDegraded, exception, fields);
        _entries[battleId] = new PayloadFailureEntry(fingerprint, reasonCode, NextSequence());
    }

    internal void Clear(string battleId)
    {
        if (!_entries.Remove(battleId, out var previous))
            return;
        BppLog.RecoverStorm(
            HistoryPanelLogEvents.PreviewPayloadDegraded,
            HistoryPanelLogEvents.PreviewPayloadReasonCode.Bind(previous.ReasonCode)
        );
    }

    private long NextSequence() => unchecked(++_sequence);

    private void EvictLeastRecentlyUsed()
    {
        string? oldestBattleId = null;
        var oldestSequence = long.MaxValue;
        foreach (var pair in _entries)
        {
            if (pair.Value.LastTouchedSequence >= oldestSequence)
                continue;
            oldestBattleId = pair.Key;
            oldestSequence = pair.Value.LastTouchedSequence;
        }
        if (oldestBattleId != null)
            _entries.Remove(oldestBattleId);
    }

    private readonly record struct PayloadFailureEntry(
        string Fingerprint,
        HistoryPanelPreviewPayloadReasonCode ReasonCode,
        long LastTouchedSequence
    );
}
