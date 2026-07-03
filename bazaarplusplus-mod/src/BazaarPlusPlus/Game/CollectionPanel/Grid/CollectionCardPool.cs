#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.GameInterop.CardPreview;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Pools the current-game collection card unit created by AssetLoader:
// host -> socket -> native CardPreviewBase. Do not replace this with the old
// MonsterBoardTooltip prefab pool; that path no longer matches the game's UI card layout.
internal sealed class CollectionCardPool
{
    private const int DefaultMaxPoolSizePerKind = 30;

    private readonly int _maxPoolSizePerKind;
    private readonly Dictionary<NativeCardPreviewKind, Queue<CollectionCardBinding>> _pool = new();

    public CollectionCardPool(int maxPoolSizePerKind = DefaultMaxPoolSizePerKind)
    {
        _maxPoolSizePerKind = Math.Max(1, maxPoolSizePerKind);
    }

    public bool TryTake(NativeCardPreviewKind kind, out CollectionCardBinding binding)
    {
        binding = null!;
        if (!_pool.TryGetValue(kind, out var queue))
            return false;

        while (queue.Count > 0)
        {
            var candidate = queue.Dequeue();
            if (candidate is { IsDestroyed: false })
            {
                binding = candidate;
                return true;
            }
        }

        return false;
    }

    public void Return(CollectionCardBinding? binding)
    {
        if (binding == null || binding.IsDestroyed)
            return;

        if (!_pool.TryGetValue(binding.Kind, out var queue))
        {
            queue = new Queue<CollectionCardBinding>();
            _pool[binding.Kind] = queue;
        }

        if (queue.Count >= _maxPoolSizePerKind)
        {
            var evicted = queue.Dequeue();
            evicted?.DestroyHost();
        }

        queue.Enqueue(binding);
    }

    public void DestroyAll()
    {
        foreach (var queue in _pool.Values)
        {
            while (queue.Count > 0)
                queue.Dequeue()?.DestroyHost();
        }

        _pool.Clear();
    }
}
