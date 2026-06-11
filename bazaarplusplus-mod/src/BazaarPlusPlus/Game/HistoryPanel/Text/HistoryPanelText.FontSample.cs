#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static readonly Dictionary<string, string> FontAtlasSampleCache = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal static string FontProbeSample()
    {
        return FormatSimple("Game History Replay", "对局历史回放");
    }

    internal static string FontAtlasSample()
    {
        var languageCode = L.CurrentLanguageCode;
        var mode = L.CurrentMode;
        var cacheKey = CreateFontAtlasSampleCacheKey(languageCode, mode);
        if (FontAtlasSampleCache.TryGetValue(cacheKey, out var cachedSample))
            return cachedSample;

        var parts = new List<string>();
        foreach (
            var field in typeof(HistoryPanelText).GetFields(
                BindingFlags.Static | BindingFlags.NonPublic
            )
        )
        {
            if (field.FieldType != typeof(LocalizedTextSet))
                continue;

            if (field.GetValue(null) is not LocalizedTextSet set)
                continue;

            var resolved = set.Resolve(languageCode, mode);
            if (!string.IsNullOrWhiteSpace(resolved))
                parts.Add(resolved);
        }

        parts.Add(
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz -_:/?()[]{}%+,.!|#"
        );

        var deduped = new HashSet<char>();
        foreach (var part in parts)
        {
            foreach (var character in part)
            {
                if (!char.IsControl(character))
                    deduped.Add(character);
            }
        }

        var sample = deduped.Count > 0 ? new string(deduped.ToArray()) : FontProbeSample();
        FontAtlasSampleCache[cacheKey] = sample;
        return sample;
    }

    private static string CreateFontAtlasSampleCacheKey(
        string languageCode,
        BppChineseLocaleMode mode
    )
    {
        return $"{languageCode}\u001F{(int)mode}";
    }
}
