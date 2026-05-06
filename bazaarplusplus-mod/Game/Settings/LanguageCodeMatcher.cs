using System;

namespace BazaarPlusPlus.Game.Settings;

internal static class LanguageCodeMatcher
{
    internal static bool IsChinese(string languageCode) =>
        Matches(
            languageCode,
            "zh",
            "zh-CN",
            "zh-Hans",
            "zh-SG",
            "zh-TW",
            "zh-Hant",
            "zh-HK",
            "zh-MO"
        );

    internal static bool IsSimplifiedChinese(string languageCode) =>
        Matches(languageCode, "zh-CN", "zh-Hans", "zh-SG");

    internal static bool IsGerman(string languageCode) =>
        Matches(languageCode, "de", "de-DE", "de-AT", "de-CH");

    internal static bool IsPortuguese(string languageCode) =>
        Matches(languageCode, "pt", "pt-BR", "pt-PT");

    internal static bool IsKorean(string languageCode) => Matches(languageCode, "ko", "ko-KR");

    internal static bool IsItalian(string languageCode) => Matches(languageCode, "it", "it-IT");

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
