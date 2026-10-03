#nullable enable

using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Localization;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class FontDiagnostics
{
    private const string ChineseSample = "中文字体测试商人英雄价格";
    private static bool _logged;
    private static bool _traditionalFallbackLogged;

    public static bool HasChineseCoverage(TMP_FontAsset? font)
    {
        return HasFullChineseCoverage(font);
    }

    // Whether the font, including its fallback chain, can render a character. Dynamic atlases add
    // glyphs on demand when text renders, so the probe may add one too; a Simplified-only probe
    // with tryAddCharacter off would report such glyphs as missing.
    public static Func<char, bool> GlyphProbe(TMP_FontAsset? font) =>
        character =>
            font == null
            || font.HasCharacter(character, searchFallbacks: true, tryAddCharacter: true);

    public static void LogTraditionalFallbackOnce(TextMeshProUGUI renderer, string simplifiedLine)
    {
        if (_traditionalFallbackLogged)
            return;

        _traditionalFallbackLogged = true;
        var font = renderer.font;
        var converted = L.ResolveChinese(simplifiedLine);
        var probe = GlyphProbe(font);
        var added = converted.Where(character => simplifiedLine.IndexOf(character) < 0).ToArray();
        BppLog.WarnEvent(
            new BppLogEvent(
                BppLogFeatureScope.VoiceSubtitles,
                "voice_subtitles.font_environment.observed"
            ),
            ("reason_code", VoiceSubtitlesLogReasonCode.TraditionalGlyphsMissing),
            ("anchor_path", BuildPath(renderer.transform)),
            ("source_font", DescribeFont(font)),
            ("source_coverage", $"{added.Count(probe)}/{added.Length}"),
            ("default_font", DescribeFont(TMP_Settings.defaultFontAsset)),
            ("fallback_fonts", DescribeFontList(TMP_Settings.fallbackFontAssets))
        );
    }

    public static void LogOnce(TextMeshProUGUI sourceLabel)
    {
        if (_logged)
            return;

        _logged = true;

        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.VoiceSubtitles,
                "voice_subtitles.font_environment.observed"
            ),
            () =>
            {
                var sourceFont = sourceLabel.font;
                return
                [
                    ("reason_code", VoiceSubtitlesLogReasonCode.Mount),
                    ("anchor_path", BuildPath(sourceLabel.transform)),
                    ("source_font", DescribeFont(sourceFont)),
                    ("source_coverage", DescribeCoverage(sourceFont)),
                    ("default_font", DescribeFont(TMP_Settings.defaultFontAsset)),
                    ("fallback_fonts", DescribeFontList(TMP_Settings.fallbackFontAssets)),
                ];
            }
        );
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.VoiceSubtitles,
                "voice_subtitles.font_inventory.observed"
            ),
            () =>
            {
                var loadedFonts = Resources
                    .FindObjectsOfTypeAll<TMP_FontAsset>()
                    .Where(font => font != null)
                    .GroupBy(font => font.GetInstanceID())
                    .Select(group => group.First())
                    .OrderByDescending(ChineseCoverageCount)
                    .ThenBy(font => font.name, StringComparer.OrdinalIgnoreCase)
                    .Take(24)
                    .ToArray();
                return
                [
                    ("font_count", loadedFonts.Length),
                    ("fonts", DescribeScoredFonts(loadedFonts)),
                ];
            }
        );
    }

    private static string DescribeScoredFonts(IReadOnlyList<TMP_FontAsset> fonts)
    {
        if (fonts.Count == 0)
            return "<none>";

        return string.Join(
            "; ",
            fonts.Select(font => $"{DescribeFont(font)} coverage={DescribeCoverage(font)}")
        );
    }

    private static string DescribeFontList(IReadOnlyList<TMP_FontAsset>? fonts)
    {
        if (fonts == null || fonts.Count == 0)
            return "<none>";

        return string.Join(", ", fonts.Where(font => font != null).Select(DescribeFont));
    }

    public static string DescribeFont(TMP_FontAsset? font)
    {
        if (font == null)
            return "<null>";

        return $"'{font.name}'#{font.GetInstanceID()}";
    }

    private static string DescribeCoverage(TMP_FontAsset? font)
    {
        if (font == null)
            return "0/0";

        return $"{ChineseCoverageCount(font)}/{ChineseSample.Length}";
    }

    private static int ChineseCoverageCount(TMP_FontAsset? font)
    {
        if (font == null)
            return 0;

        var count = 0;
        foreach (var character in ChineseSample)
        {
            if (font.HasCharacter(character, searchFallbacks: true, tryAddCharacter: false))
                count++;
        }

        return count;
    }

    private static bool HasFullChineseCoverage(TMP_FontAsset? font)
    {
        return font != null && ChineseCoverageCount(font) == ChineseSample.Length;
    }

    private static string BuildPath(Transform transform)
    {
        var path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
