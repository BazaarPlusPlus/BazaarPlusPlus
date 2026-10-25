#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Supporters;

public static class BPPSupporterLinks
{
    private const string ChineseSponsorUrl = "https://bazaarplusplus.com/support";
    private const string EnglishSponsorUrl = "https://bazaarplusplus.com/support?lang=en";

    public static string ResolveSponsorUrl(string languageCode)
    {
        return LanguageCodeMatcher.IsChinese(languageCode) ? ChineseSponsorUrl : EnglishSponsorUrl;
    }
}
