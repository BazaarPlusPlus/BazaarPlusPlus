#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.GameInterop.CardArtReplacement;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal sealed class CustomCardArtMaterialCache : IDisposable
{
    private const string LogCategory = "CardArtReplacement";

    private readonly Dictionary<
        (Guid templateId, int baseMaterialInstanceId),
        Material
    > _materialsByKey = new();

    public int CachedCount => _materialsByKey.Count;

    public bool TryGetMaterial(
        Guid templateId,
        Material baseMaterial,
        Texture2D texture,
        out Material? material
    )
    {
        material = null;
        if (templateId == Guid.Empty || baseMaterial == null || texture == null)
            return false;

        var key = (templateId, baseMaterial.GetInstanceID());
        if (_materialsByKey.TryGetValue(key, out material) && !ReferenceEquals(material, null))
            return true;

        try
        {
            material = new Material(baseMaterial)
            {
                name = $"BPP_CustomCardArtMaterial_{templateId}",
            };
            if (!CardArtInjector.Apply(material, texture))
            {
                Object.Destroy(material);
                material = null;
                return false;
            }

            _materialsByKey[key] = material;
            return true;
        }
        catch (Exception ex)
        {
            if (material != null)
                Object.Destroy(material);
            BppLog.Warn(
                LogCategory,
                $"Failed to create custom preview material templateId={templateId}: {ex.Message}"
            );
            material = null;
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var material in _materialsByKey.Values)
        {
            if (material != null)
                Object.Destroy(material);
        }

        _materialsByKey.Clear();
    }
}
