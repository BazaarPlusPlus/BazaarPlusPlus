#nullable enable
using System;
using System.IO;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Storage.Paths;
using UnityEngine;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal sealed class CardArtReplacementFeature : IBppFeature
{
    private const string LogCategory = "CardArtReplacement";

    private readonly IPathProvider _paths;
    private CustomCardArtCatalog? _catalog;
    private CustomCardArtTextureCache? _textureCache;
    private CustomCardArtMaterialCache? _materialCache;

    public CardArtReplacementFeature(IPathProvider paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public static CardArtReplacementFeature? Current { get; private set; }

    public int CatalogCount => _catalog?.Count ?? 0;

    public void Start()
    {
        var directoryPath = _paths.CustomCardArtDirectoryPath;
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            BppLog.Warn(LogCategory, "Custom card art directory path is unavailable.");
            return;
        }

        try
        {
            Directory.CreateDirectory(directoryPath);
            var installResult = new BundledCustomCardArtInstaller().InstallMissing(directoryPath);
            if (installResult.ResourceCount > 0)
            {
                BppLog.Info(
                    LogCategory,
                    "Bundled custom card art checked "
                        + $"resources={installResult.ResourceCount} "
                        + $"written={installResult.WrittenCount} "
                        + $"existing={installResult.ExistingCount} "
                        + $"failed={installResult.FailedCount} "
                        + $"path='{directoryPath}'"
                );
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                LogCategory,
                $"Failed to prepare custom card art directory '{directoryPath}': {ex.Message}"
            );
        }

        _catalog = new CustomCardArtCatalog(directoryPath);
        _textureCache = new CustomCardArtTextureCache(_catalog);
        _materialCache = new CustomCardArtMaterialCache();
        Current = this;
        BppLog.Info(
            LogCategory,
            $"Custom card art catalog ready count={_catalog.Count} path='{directoryPath}'"
        );
    }

    public void Stop()
    {
        if (ReferenceEquals(Current, this))
            Current = null;

        _textureCache?.Dispose();
        _materialCache?.Dispose();
        _textureCache = null;
        _materialCache = null;
        _catalog = null;
    }

    public bool TryGetTexture(Guid templateId, out Texture2D? texture, out string? sourcePath)
    {
        if (_textureCache == null)
        {
            texture = null;
            sourcePath = null;
            return false;
        }

        return _textureCache.TryGetTexture(templateId, out texture, out sourcePath);
    }

    public bool TryGetPreviewMaterial(
        Guid templateId,
        Material baseMaterial,
        out Material? material,
        out string? sourcePath
    )
    {
        material = null;
        if (_textureCache == null || _materialCache == null)
        {
            sourcePath = null;
            return false;
        }

        if (
            !_textureCache.TryGetTexture(templateId, out var texture, out sourcePath)
            || texture == null
        )
            return false;

        return _materialCache.TryGetMaterial(templateId, baseMaterial, texture, out material);
    }
}
