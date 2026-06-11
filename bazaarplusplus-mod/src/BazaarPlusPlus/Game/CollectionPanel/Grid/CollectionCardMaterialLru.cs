#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

internal sealed class CollectionCardMaterialLru
{
    private readonly int _capacity;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodeMap = new(
        StringComparer.Ordinal
    );

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
            var node = _lru.AddFirst(key);
            _nodeMap[key] = node;
            entry = new Entry();
            _entries[key] = entry;
        }
        else
        {
            Touch(key);
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
        _nodeMap.Clear();
    }

    private IReadOnlyList<string> Evict()
    {
        List<string>? evicted = null;
        while (_entries.Count > _capacity)
        {
            var node = _lru.Last;
            string? evictKey = null;
            while (node != null)
            {
                if (_entries.TryGetValue(node.Value, out var entry) && entry.RefCount <= 0)
                {
                    evictKey = node.Value;
                    break;
                }
                node = node.Previous;
            }

            if (evictKey == null)
                break;

            Remove(evictKey);
            evicted ??= new List<string>();
            evicted.Add(evictKey);
        }

        return evicted ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    private void Remove(string key)
    {
        _entries.Remove(key);
        if (_nodeMap.TryGetValue(key, out var node))
        {
            _nodeMap.Remove(key);
            _lru.Remove(node);
        }
    }

    private void Touch(string key)
    {
        if (!_nodeMap.TryGetValue(key, out var node))
            return;
        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private sealed class Entry
    {
        public int RefCount { get; set; }
    }
}
