#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.Utilities.Shaders;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// uGUI batches by material instance. The Harmony destruction prefix clears cached materials before the game's OnDestroy can destroy them.
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

    public async Task<Material?> LoadSource(string artKey, CardAssetDataSO assetData)
    {
        if (_materials.TryGetValue(artKey, out var cached) && cached != null)
            return cached;
        return await assetData.LoadCollectionDetailsAssetAsync() as Material;
    }

    public Material? GetOrCreate(
        string artKey,
        Material? cardMaterial,
        Shader? shaderOverride,
        bool usePremiumVisuals
    )
    {
        if (string.IsNullOrEmpty(artKey) || cardMaterial == null)
            return null;

        if (_materials.TryGetValue(artKey, out var cached) && cached != null)
        {
            ApplyVisualMode(cached, usePremiumVisuals);
            return cached;
        }

        var material = new Material(cardMaterial);
        material.DisableKeyword("_ISTINY_ON");
        if (shaderOverride != null)
            material.shader = shaderOverride;
        ApplyVisualMode(material, usePremiumVisuals);
        material.name = $"CollectionPanelMaterial[{artKey}]";
        _materials[artKey] = material;
        return material;
    }

    private static void ApplyVisualMode(Material material, bool usePremiumVisuals)
    {
        if (usePremiumVisuals)
            material.EnableKeyword(CardArtShaderVariables.PremiumShaderKeyword);
        else
            material.DisableKeyword(CardArtShaderVariables.PremiumShaderKeyword);
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
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CollectionPanel,
                        "collection_panel.cache.cleanup_failed"
                    ),
                    ex,
                    () =>
                        [
                            ("cache", CollectionCacheKind.Material),
                            ("stage", CollectionCacheCleanupStage.Destroy),
                            ("art_key", pair.Key),
                        ]
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
                BppLog.DebugEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.CollectionPanel,
                        "collection_panel.cache.cleanup_failed"
                    ),
                    ex,
                    () =>
                        [
                            ("cache", CollectionCacheKind.Material),
                            ("stage", CollectionCacheCleanupStage.EvictDestroy),
                            ("art_key", artKey),
                        ]
                );
            }
        }
    }
}
