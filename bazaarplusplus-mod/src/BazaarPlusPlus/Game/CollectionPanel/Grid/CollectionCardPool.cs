#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarPlusPlus.GameInterop.CardPreview;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Per-kind pool of CardPreviewBase instances created via AssetLoader.InstantiateUICardAsync.
// Pool hits are dequeued and reparented synchronously; pool misses await the AssetLoader
// call on the first Take for each kind, after which the card is tagged with
// CollectionPanelOwnedMarker so the cached LoadArt path activates on every subsequent
// SetUp (pool reuse).
internal sealed class CollectionCardPool
{
    private const int DefaultMaxPoolSizePerKind = 30;

    private readonly int _layer;
    private readonly int _maxPoolSizePerKind;
    private readonly Dictionary<NativeCardPreviewKind, Queue<Component>> _pool = new();

    public CollectionCardPool(int layer, int maxPoolSizePerKind = DefaultMaxPoolSizePerKind)
    {
        _layer = layer;
        _maxPoolSizePerKind = Math.Max(1, maxPoolSizePerKind);
    }

    public bool TryEnsurePrefabRefs() =>
        NativeCardPreviewPrefabResolver.TryEnsureResolved(
            requireSkill: true,
            requireSockets: false,
            "CollectionCardPool"
        );

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
                "CollectionCardPool"
            );
            if (card == null)
                return (null, false);

            card.name = $"CollectionPanelCard_{kind}";
            // Marker gates CollectionItemLoadArtPatch and survives every Take/Return cycle.
            if (card.gameObject.GetComponent<CollectionPanelOwnedMarker>() == null)
                card.gameObject.AddComponent<CollectionPanelOwnedMarker>();
            // CanvasGroup drives the per-card fade-in; alpha reset to 0 each Take.
            if (card.gameObject.GetComponent<CanvasGroup>() == null)
                card.gameObject.AddComponent<CanvasGroup>();

        }
        else
        {
            card!.transform.SetParent(parent, worldPositionStays: false);
        }

        card!.transform.localScale = Vector3.one;
        card.transform.localRotation = Quaternion.identity;
        card.gameObject.SetActive(true);
        NativeCardPreviewReflection.ApplyLayerRecursive(card.gameObject, _layer);

        var canvasGroup = card.gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        NativeCardPreviewRuntime.Resize(card, "CollectionCardPool");
        return (card, isNew);
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
