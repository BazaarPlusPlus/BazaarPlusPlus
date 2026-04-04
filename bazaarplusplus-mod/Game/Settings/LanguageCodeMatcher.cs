using System;

namespace BazaarPlusPlus.Game.Settings;

internal static class LanguageCodeMatcher
{
    internal static bool IsSimplifiedChinese(string languageCode) =>
        Matches(languageCode, "zh-CN", "zh-Hans", "zh");

    internal static bool IsGerman(string languageCode) => Matches(languageCode, "de-DE", "de");

    internal static bool IsPortuguese(string languageCode) =>
        Matches(languageCode, "pt-BR", "pt-PT", "pt");

    internal static bool IsKorean(string languageCode) => Matches(languageCode, "ko-KR", "ko");

    internal static bool IsItalian(string languageCode) => Matches(languageCode, "it-IT", "it");

    private static bool Matches(string languageCode, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        var normalized = languageCode.Replace('_', '-');
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalized.StartsWith(candidate + "-", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
