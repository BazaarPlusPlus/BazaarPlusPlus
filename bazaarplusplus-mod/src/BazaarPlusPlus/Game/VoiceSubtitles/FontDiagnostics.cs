#nullable enable

using BazaarPlusPlus.Infrastructure;
using TMPro;
using UnityEngine;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class FontDiagnostics
{
    private const string ChineseSample = "中文字体测试商人英雄价格";
    private static bool _logged;

    public static bool HasChineseCoverage(TMP_FontAsset? font)
    {
        return HasFullChineseCoverage(font);
    }

    public static void LogOnce(TextMeshProUGUI sourceLabel)
    {
        if (_logged)
            return;

        _logged = true;

        BppLog.DebugEvent(
            VoiceSubtitlesDisplayLogEvents.FontEnvironmentObserved,
            () =>
            {
                var sourceFont = sourceLabel.font;
                return
                [
                    VoiceSubtitlesDisplayLogEvents.FontEnvironmentReasonCode.Bind(
                        VoiceSubtitlesLogReasonCode.Mount
                    ),
                    VoiceSubtitlesDisplayLogEvents.FontEnvironmentAnchorPath.Bind(
                        BuildPath(sourceLabel.transform)
                    ),
                    VoiceSubtitlesDisplayLogEvents.FontEnvironmentSourceFont.Bind(
                        DescribeFont(sourceFont)
                    ),
                    VoiceSubtitlesDisplayLogEvents.FontEnvironmentSourceCoverage.Bind(
                        DescribeCoverage(sourceFont)
                    ),
                    VoiceSubtitlesDisplayLogEvents.FontEnvironmentDefaultFont.Bind(
                        DescribeFont(TMP_Settings.defaultFontAsset)
                    ),
                    VoiceSubtitlesDisplayLogEvents.FontEnvironmentFallbackFonts.Bind(
                        DescribeFontList(TMP_Settings.fallbackFontAssets)
                    ),
                ];
            }
        );
        BppLog.DebugEvent(
            VoiceSubtitlesDisplayLogEvents.FontInventoryObserved,
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
                    VoiceSubtitlesDisplayLogEvents.FontInventoryCount.Bind(loadedFonts.Length),
                    VoiceSubtitlesDisplayLogEvents.FontInventoryFonts.Bind(
                        DescribeScoredFonts(loadedFonts)
                    ),
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
