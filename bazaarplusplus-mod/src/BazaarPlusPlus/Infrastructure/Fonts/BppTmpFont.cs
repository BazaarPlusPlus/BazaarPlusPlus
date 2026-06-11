#nullable enable
using System;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace BazaarPlusPlus.Infrastructure.Fonts;

internal static class BppTmpFont
{
    private const string Component = "TmpFont";
    private const int SamplingPointSize = 90;
    private const int AtlasPadding = 9;
    private const int AtlasSize = 2048;

    private static TMP_FontAsset? _default;
    private static bool _loadFailureLogged;
    private static readonly ConditionalWeakTable<TMP_Text, FontSnapshot> OriginalFonts = new();

    public static bool TryApply(TMP_Text? text, string? sampleText)
    {
        if (text == null)
            return false;

        if (!BppTmpFontPolicy.ShouldUseEmbeddedCjkFont(sampleText))
        {
            RestoreOriginal(text);
            return false;
        }

        var fontAsset = ResolveDefault();
        if (fontAsset == null)
            return false;

        CaptureOriginal(text);
        text.font = fontAsset;
        if (fontAsset.material != null)
            text.fontSharedMaterial = fontAsset.material;

        WarmCharacters(fontAsset, sampleText);
        return true;
    }

    private static TMP_FontAsset? ResolveDefault()
    {
        if (_default != null)
            return _default;

        try
        {
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                BppUiFont.Default,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: true
            );
            if (fontAsset == null)
            {
                LogLoadFailure("CreateFontAsset returned null.");
                return null;
            }

            fontAsset.name = "BPP LXGWWenKai TMP";
            _default = fontAsset;
            BppLog.Info(Component, $"Loaded TMP UI font '{fontAsset.name}'.");
            return _default;
        }
        catch (Exception ex)
        {
            LogLoadFailure(ex.Message);
            return null;
        }
    }

    private static void WarmCharacters(TMP_FontAsset fontAsset, string? sampleText)
    {
        if (string.IsNullOrEmpty(sampleText))
            return;

        try
        {
            fontAsset.TryAddCharacters(sampleText, out _);
        }
        catch (Exception ex)
        {
            BppLog.Debug(Component, $"Failed to warm TMP glyphs: {ex.Message}");
        }
    }

    private static void CaptureOriginal(TMP_Text text)
    {
        if (OriginalFonts.TryGetValue(text, out _))
            return;

        OriginalFonts.Add(
            text,
            new FontSnapshot { Font = text.font, SharedMaterial = text.fontSharedMaterial }
        );
    }

    private static void RestoreOriginal(TMP_Text text)
    {
        if (!OriginalFonts.TryGetValue(text, out var snapshot))
            return;

        if (snapshot.Font != null)
            text.font = snapshot.Font;
        if (snapshot.SharedMaterial != null)
            text.fontSharedMaterial = snapshot.SharedMaterial;
    }

    private static void LogLoadFailure(string reason)
    {
        if (_loadFailureLogged)
            return;

        _loadFailureLogged = true;
        BppLog.Warn(Component, $"Failed to load embedded TMP UI font. {reason}");
    }

    private sealed class FontSnapshot
    {
        public TMP_FontAsset? Font { get; init; }
        public Material? SharedMaterial { get; init; }
    }
}
