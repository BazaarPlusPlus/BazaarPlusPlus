#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.GameInterop.CardPreview;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Per-kind pool of CardPreviewBase instances cloned from MonsterBoardTooltip's four prefab
// fields (_smallItemReference / _mediumItemReference / _largeItemReference / _skillReference).
// Mirrors the native card-preview Take/Return/eviction shape, but is keyed by
// (ECardType, ECardSize) instead of just ECardSize so the Skill prefab can be served without
// pretending it is an Item.
//
// Prefab refs are resolved via reflection through Harmony.AccessTools to keep the mod
// resilient to future game-side renames; if the lookup fails the panel logs and stays
// closed rather than crashing.
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

    public Component? Take(NativeCardPreviewKind kind, Transform parent)
    {
        if (!TryEnsurePrefabRefs())
            return null;

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

        if (card == null)
        {
            if (
                !NativeCardPreviewPrefabResolver.TryGetPrefab(
                    kind,
                    requireSkill: true,
                    requireSockets: false,
                    "CollectionCardPool",
                    out var prefab
                )
                || prefab == null
            )
                return null;

            card = Object.Instantiate(prefab, parent, worldPositionStays: false);
            if (card != null)
            {
                card.name = $"CollectionPanelCard_{kind}";
                // Stamp the marker once at instantiation; the OnDestroy + LoadArt patches use
                // it to gate cache participation, and the marker survives every Take/Return
                // cycle so the gating stays consistent across pool reuse.
                if (card.gameObject.GetComponent<CollectionPanelOwnedMarker>() == null)
                    card.gameObject.AddComponent<CollectionPanelOwnedMarker>();
                // CanvasGroup drives the per-card fade-in. Added once at instantiation;
                // alpha is reset to 0 below on every Take so each rebind starts invisible
                // and the virtualizer's TickFades ramps it back up to 1 after Show.
                if (card.gameObject.GetComponent<CanvasGroup>() == null)
                    card.gameObject.AddComponent<CanvasGroup>();
            }
        }
        else
        {
            card.transform.SetParent(parent, worldPositionStays: false);
        }

        if (card == null)
            return null;

        card.transform.localScale = Vector3.one;
        card.transform.localRotation = Quaternion.identity;
        card.gameObject.SetActive(true);
        NativeCardPreviewReflection.ApplyLayerRecursive(card.gameObject, _layer);

        // Start each (re)bind invisible so the fade ramp owns the perceived appearance.
        var canvasGroup = card.gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        NativeCardPreviewRuntime.Resize(card, "CollectionCardPool");

        return card;
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
