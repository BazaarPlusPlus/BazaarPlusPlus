#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Infrastructure;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// L3 of the design's four-layer cache stack (§7). CardPreviewItem.UpdateCardImageMaterial
// allocates a new Material per Item card; for 40+ Items visible at once that means as many
// distinct Materials and no uGUI dynamic batching. With this cache the same artKey hands out
// one shared Material instance across all Item cards, so all cells with the same art (and
// non-premium / no-enchant) share a draw call.
//
// Lifecycle: shared Materials are owned by the cache. The collection panel's per-card
// OnDestroy Harmony prefix nulls the card's _cardMaterial field before the original
// OnDestroy fires, so the game's defensive `if (_cardMaterial) Destroy(_cardMaterial)` is a
// no-op for our tracked cards. DisposeAll() actually destroys the Materials when the panel
// itself is torn down (scene change or panel mount unmounts).
internal sealed class CollectionCardMaterialCache
{
    private const int DefaultCapacity = 256;

    private readonly Dictionary<string, Material> _materials = new();
    private readonly CollectionCardMaterialLru _lru;

    public CollectionCardMaterialCache(int capacity = DefaultCapacity)
    {
        _lru = new CollectionCardMaterialLru(capacity);
    }

    public void Acquire(string artKey)
    {
        DestroyEvicted(_lru.Acquire(artKey));
    }

    public void Release(string artKey)
    {
        _lru.Release(artKey);
    }

    public Material? GetOrCreate(string artKey, CardAssetDataSO assetData, Shader? shaderOverride)
    {
        if (string.IsNullOrEmpty(artKey) || assetData == null || assetData.cardMaterial == null)
            return null;

        if (_materials.TryGetValue(artKey, out var cached) && cached != null)
            return cached;

        var material = new Material(assetData.cardMaterial);
        if (shaderOverride != null)
            material.shader = shaderOverride;
        material.name = $"CollectionPanelMaterial[{artKey}]";
        _materials[artKey] = material;
        return material;
    }

    public bool Contains(Material? material)
    {
        if (material == null)
            return false;
        foreach (var cached in _materials.Values)
        {
            if (ReferenceEquals(cached, material))
                return true;
        }
        return false;
    }

    public void DisposeAll()
    {
        foreach (var pair in _materials)
        {
            try
            {
                if (pair.Value != null)
                    Object.Destroy(pair.Value);
            }
            catch (System.Exception ex)
            {
                BppLog.Debug(
                    "CollectionCardMaterialCache",
                    $"Destroy failed for artKey='{pair.Key}': {ex.Message}"
                );
            }
        }
        _materials.Clear();
        _lru.Clear();
    }

    private void DestroyEvicted(IReadOnlyList<string> evictedKeys)
    {
        foreach (var artKey in evictedKeys)
        {
            if (!_materials.TryGetValue(artKey, out var material))
                continue;
            _materials.Remove(artKey);
            try
            {
                if (material != null)
                    Object.Destroy(material);
            }
            catch (System.Exception ex)
            {
                BppLog.Debug(
                    "CollectionCardMaterialCache",
                    $"Evict Destroy failed for artKey='{artKey}': {ex.Message}"
                );
            }
        }
    }
}
