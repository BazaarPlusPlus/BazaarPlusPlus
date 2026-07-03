#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewPool
{
    private const int DefaultMaxPoolSizePerKind = 30;

    private readonly int _layer;
    private readonly bool _requireSockets;
    private readonly string _logComponent;
    private readonly int _maxPoolSizePerKind;
    private readonly Dictionary<NativeCardPreviewKind, Queue<Component>> _pool = new();

    public NativeCardPreviewPool(
        int layer,
        bool requireSockets,
        string logComponent,
        int maxPoolSizePerKind = DefaultMaxPoolSizePerKind
    )
    {
        _layer = layer;
        _requireSockets = requireSockets;
        _logComponent = string.IsNullOrWhiteSpace(logComponent)
            ? "NativeCardPreviewPool"
            : logComponent;
        _maxPoolSizePerKind = Math.Max(1, maxPoolSizePerKind);
    }

    public bool TryEnsurePrefabRefs(bool requireSkill)
    {
        return NativeCardPreviewPrefabResolver.TryEnsureResolved(
            requireSkill,
            _requireSockets,
            _logComponent
        );
    }

    // Returns (card, isNew). isNew=true means the card was freshly created by InstantiateUICardAsync
    // and already has SetUp called internally; callers must NOT call SetUp again for new cards.
    // isNew=false means the card was recycled from the pool and needs SetUp for rebinding.
    public async Task<(Component? card, bool isNew)> TakeAsync(
        NativeCardPreviewKind kind,
        TCardInstance instance,
        Transform parent
    )
    {
        if (!_pool.TryGetValue(kind, out var queue))
        {
            queue = new Queue<Component>();
            _pool[kind] = queue;
        }

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

        var isNew = card == null;
        if (isNew)
        {
            card = await NativeCardPreviewPrefabResolver.TryCreateCardAsync(
                instance,
                parent,
                _logComponent
            );
            if (card == null)
                return (null, false);
            card.name = $"BppNativeCardPreview_{kind}";
        }
        else
        {
            card!.transform.SetParent(parent, worldPositionStays: false);
        }

        card!.transform.localScale = Vector3.one;
        card.transform.localRotation = Quaternion.identity;
        card.gameObject.SetActive(true);
        NativeCardPreviewReflection.ApplyLayerRecursive(card.gameObject, _layer);
        NativeCardPreviewRuntime.Resize(card, _logComponent);
        return (card, isNew);
    }

    public void Return(NativeCardPreviewHandle? handle)
    {
        if (handle == null)
            return;
        Return(handle.Card, handle.Kind);
    }

    public void Return(Component? card, NativeCardPreviewKind kind)
    {
        if (card == null)
            return;

        card.gameObject.SetActive(false);
        if (!_pool.TryGetValue(kind, out var queue))
        {
            queue = new Queue<Component>();
            _pool[kind] = queue;
        }

        if (queue.Count >= _maxPoolSizePerKind)
        {
            var evicted = queue.Dequeue();
            if (evicted != null)
                Object.Destroy(evicted.gameObject);
        }

        queue.Enqueue(card);
    }

    public void DestroyAll()
    {
        foreach (var queue in _pool.Values)
        {
            while (queue.Count > 0)
            {
                var card = queue.Dequeue();
                if (card != null)
                    Object.Destroy(card.gameObject);
            }
        }

        _pool.Clear();
    }
}
