#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal sealed class CollectionCardMaterialLru
{
    private readonly int _capacity;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();

    public CollectionCardMaterialLru(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public int Count => _entries.Count;

    public IReadOnlyList<string> Acquire(string key)
    {
        if (string.IsNullOrEmpty(key))
            return Array.Empty<string>();

        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry(_lru.AddFirst(key));
            _entries[key] = entry;
        }
        else
        {
            _lru.Remove(entry.Node);
            _lru.AddFirst(entry.Node);
        }

        entry.RefCount++;
        return Evict();
    }

    public void Release(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;
        if (_entries.TryGetValue(key, out var entry))
            entry.RefCount = Math.Max(0, entry.RefCount - 1);
    }

    public bool Contains(string key) => _entries.ContainsKey(key);

    public void Clear()
    {
        _entries.Clear();
        _lru.Clear();
    }

    private IReadOnlyList<string> Evict()
    {
        List<string>? evicted = null;
        while (_entries.Count > _capacity)
        {
            var node = _lru.Last;
            Entry? evictEntry = null;
            string? evictKey = null;
            while (node != null)
            {
                if (_entries.TryGetValue(node.Value, out var entry) && entry.RefCount <= 0)
                {
                    evictEntry = entry;
                    evictKey = node.Value;
                    break;
                }
                node = node.Previous;
            }

            if (evictKey == null || evictEntry == null)
                break;

            _entries.Remove(evictKey);
            _lru.Remove(evictEntry.Node);
            evicted ??= new List<string>();
            evicted.Add(evictKey);
        }

        return evicted ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    private sealed class Entry
    {
        public Entry(LinkedListNode<string> node)
        {
            Node = node;
        }

        public LinkedListNode<string> Node { get; }

        public int RefCount { get; set; }
    }
}
