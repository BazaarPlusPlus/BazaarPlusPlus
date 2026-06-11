#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterAttributionText
{
    public static string FormatSupportedByPrefix(string languageCode)
    {
        return LanguageCodeMatcher.IsChinese(languageCode) ? "由" : "Supported by";
    }

    public static string FormatSupportedBySuffix(string languageCode)
    {
        return LanguageCodeMatcher.IsChinese(languageCode) ? "支持" : string.Empty;
    }

    public static string FormatSponsorAction(string languageCode)
    {
        return LanguageCodeMatcher.IsChinese(languageCode) ? "赞助" : "Sponsor";
    }

    public static string FormatSupportedBy(string supporterName, string languageCode)
    {
        if (string.IsNullOrWhiteSpace(supporterName))
            return string.Empty;

        var trimmedName = supporterName.Trim();
        return LanguageCodeMatcher.IsChinese(languageCode)
            ? $"{FormatSupportedByPrefix(languageCode)} {trimmedName} {FormatSupportedBySuffix(languageCode)}"
            : $"{FormatSupportedByPrefix(languageCode)} {trimmedName}";
    }
}
