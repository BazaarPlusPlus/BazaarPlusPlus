#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Supporters;

internal static class BPPSupporterLinks
{
    private const string ChineseSponsorUrl = "https://bazaarplusplus.com";
    private const string EnglishSponsorUrl = "https://bazaarplusplus.com/?lang=en";

    public static string ResolveSponsorUrl(string languageCode)
    {
        return LanguageCodeMatcher.IsChinese(languageCode) ? ChineseSponsorUrl : EnglishSponsorUrl;
    }
}
