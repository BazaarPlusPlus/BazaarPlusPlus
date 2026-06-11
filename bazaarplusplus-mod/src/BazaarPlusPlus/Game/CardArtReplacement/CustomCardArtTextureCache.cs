#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal sealed class CustomCardArtTextureCache : IDisposable
{
    private const string LogCategory = "CardArtReplacement";

    private readonly CustomCardArtCatalog _catalog;
    private readonly TextureLoader _loadTexture;
    private readonly Dictionary<Guid, Texture2D> _texturesByTemplateId = new();
    private readonly HashSet<Guid> _failedTemplateIds = new();

    public CustomCardArtTextureCache(CustomCardArtCatalog catalog)
        : this(catalog, TryLoadTexture) { }

    internal CustomCardArtTextureCache(CustomCardArtCatalog catalog, TextureLoader loadTexture)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _loadTexture = loadTexture ?? throw new ArgumentNullException(nameof(loadTexture));
    }

    public int CachedCount => _texturesByTemplateId.Count;

    public bool TryGetTexture(Guid templateId, out Texture2D? texture, out string? sourcePath)
    {
        sourcePath = null;
        if (templateId == Guid.Empty || _failedTemplateIds.Contains(templateId))
        {
            texture = null;
            return false;
        }

        if (
            _texturesByTemplateId.TryGetValue(templateId, out texture)
            && !ReferenceEquals(texture, null)
        )
        {
            sourcePath = _catalog.TryGetArtPath(templateId, out var cachedPath) ? cachedPath : null;
            return true;
        }

        if (!_catalog.TryGetArtPath(templateId, out var path))
        {
            texture = null;
            return false;
        }

        sourcePath = path;
        if (_loadTexture(path, templateId, out texture))
        {
            _texturesByTemplateId[templateId] = texture!;
            return true;
        }

        _failedTemplateIds.Add(templateId);
        return false;
    }

    public void Dispose()
    {
        foreach (var texture in _texturesByTemplateId.Values)
        {
            if (texture != null)
                Object.Destroy(texture);
        }

        _texturesByTemplateId.Clear();
        _failedTemplateIds.Clear();
    }

    internal delegate bool TextureLoader(string path, Guid templateId, out Texture2D? texture);

    private static bool TryLoadTexture(string path, Guid templateId, out Texture2D? texture)
    {
        texture = null;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                LogCategory,
                $"Failed to read custom art templateId={templateId}: {ex.Message}"
            );
            return false;
        }

        Texture2D? loaded = null;
        try
        {
            loaded = new Texture2D(2, 2, TextureFormat.ARGB32, mipChain: false)
            {
                name = $"BPP_CustomCardArt_{templateId}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            if (!loaded.LoadImage(bytes, markNonReadable: false))
            {
                Object.Destroy(loaded);
                BppLog.Warn(
                    LogCategory,
                    $"Failed to decode custom art templateId={templateId} path='{path}'"
                );
                return false;
            }

            loaded.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            texture = loaded;
            return true;
        }
        catch (Exception ex)
        {
            if (loaded != null)
                Object.Destroy(loaded);
            BppLog.Warn(
                LogCategory,
                $"Failed to load custom art templateId={templateId} path='{path}': {ex.Message}"
            );
            return false;
        }
    }
}
