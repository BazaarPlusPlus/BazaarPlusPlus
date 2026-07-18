#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel;

internal sealed class CollectionPortraitFailureGate<TKey, TReason>
    where TKey : notnull
{
    private const int DefaultMaximumEntries = 256;
    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly int _maximumEntries;
    private long _sequence;

    internal CollectionPortraitFailureGate(int maximumEntries = DefaultMaximumEntries)
    {
        _maximumEntries = maximumEntries;
    }

    internal int Count => _entries.Count;

    internal bool ShouldReport(TKey key, TReason reason)
    {
        if (_entries.TryGetValue(key, out var previous))
        {
            _entries[key] = new Entry(reason, NextSequence());
            return !EqualityComparer<TReason>.Default.Equals(previous.Reason, reason);
        }

        if (_entries.Count >= _maximumEntries)
            EvictLeastRecentlyUsed();
        _entries[key] = new Entry(reason, NextSequence());
        return true;
    }

    internal void Clear(TKey key) => _entries.Remove(key);

    private long NextSequence() => unchecked(++_sequence);

    private void EvictLeastRecentlyUsed()
    {
        var found = false;
        var oldestKey = default(TKey)!;
        var oldestSequence = long.MaxValue;
        foreach (var pair in _entries)
        {
            if (pair.Value.LastTouchedSequence >= oldestSequence)
                continue;
            found = true;
            oldestKey = pair.Key;
            oldestSequence = pair.Value.LastTouchedSequence;
        }
        if (found)
            _entries.Remove(oldestKey);
    }

    private readonly record struct Entry(TReason Reason, long LastTouchedSequence);
}
