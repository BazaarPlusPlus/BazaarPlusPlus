#nullable enable
namespace BazaarPlusPlus.Localization;

public static class LanguageCodeMatcher
{
    private static readonly string[] ChineseCodes =
    {
        "zh",
        "zh-CN",
        "zh-Hans",
        "zh-SG",
        "zh-TW",
        "zh-Hant",
        "zh-HK",
        "zh-MO",
    };

    private static readonly string[] GermanCodes = { "de", "de-DE", "de-AT", "de-CH" };

    private static readonly string[] PortugueseCodes = { "pt", "pt-BR", "pt-PT" };

    private static readonly string[] KoreanCodes = { "ko", "ko-KR" };

    private static readonly string[] ItalianCodes = { "it", "it-IT" };

    public static bool IsChinese(string languageCode) => Matches(languageCode, ChineseCodes);

    public static bool IsGerman(string languageCode) => Matches(languageCode, GermanCodes);

    public static bool IsPortuguese(string languageCode) => Matches(languageCode, PortugueseCodes);

    public static bool IsKorean(string languageCode) => Matches(languageCode, KoreanCodes);

    public static bool IsItalian(string languageCode) => Matches(languageCode, ItalianCodes);

    // A code matches a candidate when it equals it, or extends it with a `-` or `_`
    // subtag separator (`zh_CN` matches `zh`). Compares in place, without allocating.
    private static bool Matches(string languageCode, string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        foreach (var candidate in candidates)
        {
            if (languageCode.Length == candidate.Length)
            {
                if (string.Equals(languageCode, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
                continue;
            }

            if (
                languageCode.Length > candidate.Length
                && languageCode[candidate.Length] is '-' or '_'
                && languageCode.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
            )
                return true;
        }

        return false;
    }
}
