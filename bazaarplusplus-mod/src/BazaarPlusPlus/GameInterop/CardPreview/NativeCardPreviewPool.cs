#nullable enable
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewPool
{
    private const int DefaultMaxPoolSizePerKind = 30;

    private readonly int _maxPoolSizePerKind;
    private readonly Dictionary<NativeCardPreviewKind, Queue<Component>> _pool = new();

    internal NativeCardPreviewPool(int maxPoolSizePerKind = DefaultMaxPoolSizePerKind) =>
        _maxPoolSizePerKind = Math.Max(1, maxPoolSizePerKind);

    internal async Task<Component?> TakeInactiveAsync(
        NativeCardPreviewKind kind,
        Transform parent,
        Func<Task<Component?>> instantiateAsync,
        CancellationToken token = default
    )
    {
        if (parent == null)
            return null;

        var queue = QueueFor(kind);
        Component? card = null;
        while (queue.Count > 0)
        {
            var candidate = queue.Dequeue();
            if (candidate != null)
            {
                card = candidate;
                break;
            }
        }

        card ??= await instantiateAsync();
        if (card == null)
            return null;

        return NativeCardPreviewPoolSettlement.Prepare(
            card,
            candidate =>
            {
                candidate.gameObject.SetActive(false);
                candidate.transform.SetParent(parent, worldPositionStays: false);
            },
            candidate => Return(candidate, kind),
            Destroy,
            token
        );
    }

    internal void Return(Component? card, NativeCardPreviewKind kind)
    {
        if (card == null)
            return;

        card.gameObject.SetActive(false);
        var queue = QueueFor(kind);
        if (queue.Count >= _maxPoolSizePerKind)
        {
            var evicted = queue.Dequeue();
            if (evicted != null)
                Object.Destroy(evicted.gameObject);
        }

        queue.Enqueue(card);
    }

    internal static void Destroy(Component? card)
    {
        if (card != null)
            Object.Destroy(card.gameObject);
    }

    internal void DestroyAll()
    {
        foreach (var queue in _pool.Values)
        {
            while (queue.Count > 0)
                Destroy(queue.Dequeue());
        }

        _pool.Clear();
    }

    private Queue<Component> QueueFor(NativeCardPreviewKind kind)
    {
        if (_pool.TryGetValue(kind, out var queue))
            return queue;

        queue = new Queue<Component>();
        _pool[kind] = queue;
        return queue;
    }
}
