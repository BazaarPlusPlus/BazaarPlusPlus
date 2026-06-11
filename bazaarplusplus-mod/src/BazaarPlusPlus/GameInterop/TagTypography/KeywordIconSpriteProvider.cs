#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.TagTypography;

/// <summary>
/// Resolves a keyword icon name to a UITK-usable sprite. Native tooltips render keyword icons as
/// TMP sprite markup, which UI Toolkit chips do not parse, so this builds a Sprite over the TMP
/// atlas glyph rect instead. Main thread only; icons are locale-invariant and do not clear with
/// NativeTagTypography's locale cache.
/// </summary>
internal static class KeywordIconSpriteProvider
{
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissesThisPass = new(StringComparer.Ordinal);

    // Cached collection of all sprite assets discovered during the last full scan.
    // Null means no full scan has been performed yet.
    private static TMP_SpriteAsset[]? _discoveredAssets;

    // Set to true at the start of each pass; cleared after a full scan runs within that pass.
    // This ensures at most one expensive scan per BeginResolvePass call.
    private static bool _freshScanAllowedThisPass = true;

    public static void BeginResolvePass()
    {
        MissesThisPass.Clear();
        _freshScanAllowedThisPass = true;
    }

    public static Sprite? Resolve(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return null;
        if (Cache.TryGetValue(iconName, out var cached))
            return cached;
        if (MissesThisPass.Contains(iconName))
            return null;

        try
        {
            var asset = ResolveSpriteAsset(iconName);
            if (asset == null)
            {
                MissesThisPass.Add(iconName);
                return null;
            }

            var sprite = ExtractSprite(asset, iconName);
            if (sprite != null)
                Cache[iconName] = sprite;
            else
                MissesThisPass.Add(iconName);
            return sprite;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "KeywordIconSpriteProvider",
                $"Icon '{iconName}' failed to resolve: {ex.Message}"
            );
            return null;
        }
    }

    private static TMP_SpriteAsset? ResolveSpriteAsset(string iconName)
    {
        // Fast path: search already-discovered assets without allocating.
        if (_discoveredAssets != null)
        {
            foreach (var asset in _discoveredAssets)
            {
                if (asset != null && asset.GetSpriteIndexFromName(iconName) >= 0)
                    return asset;
            }

            // Cached collection doesn't have it. Allow a fresh scan only once per pass so
            // late-loaded assets (added after the last scan) can still be found.
            if (!_freshScanAllowedThisPass)
                return null;
        }

        // Full scan: collect all TMP sprite assets reachable from live TMP_Text components
        // first (higher probability of being the right asset), then any standalone assets.
        _freshScanAllowedThisPass = false;
        _discoveredAssets = CollectAllSpriteAssets();

        foreach (var asset in _discoveredAssets)
        {
            if (asset != null && asset.GetSpriteIndexFromName(iconName) >= 0)
                return asset;
        }

        return null;
    }

    private static TMP_SpriteAsset[] CollectAllSpriteAssets()
    {
        // Use a set keyed by instance ID to avoid duplicates without LINQ.
        var seen = new HashSet<int>();
        var result = new List<TMP_SpriteAsset>();

        foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            var asset = text != null ? text.spriteAsset : null;
            if (asset != null && seen.Add(asset.GetInstanceID()))
                result.Add(asset);
        }

        foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_SpriteAsset>())
        {
            if (asset != null && seen.Add(asset.GetInstanceID()))
                result.Add(asset);
        }

        return result.ToArray();
    }

    private static Sprite? ExtractSprite(TMP_SpriteAsset asset, string iconName)
    {
        var index = asset.GetSpriteIndexFromName(iconName);
        var table = asset.spriteCharacterTable;
        if (index < 0 || table == null || index >= table.Count)
            return null;

        if (table[index]?.glyph is not TMP_SpriteGlyph glyph)
            return null;

        if (glyph.sprite != null)
            return glyph.sprite;

        if (asset.spriteSheet is not Texture2D atlas)
            return null;

        var rect = glyph.glyphRect;
        if (rect.width <= 0 || rect.height <= 0)
            return null;

        return Sprite.Create(
            atlas,
            new Rect(rect.x, rect.y, rect.width, rect.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect
        );
    }
}
