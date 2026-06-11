#nullable enable
using BazaarPlusPlus.Localization;
using UnityEngine;

namespace BazaarPlusPlus.Game.Settings;

internal static class HotkeyTutorialLinks
{
    private const string ChineseTutorialUrl = "https://bazaarplusplus.com/tutorial";
    private const string EnglishTutorialUrl = "https://bazaarplusplus.com/tutorial?lang=en";

    internal static string ResolveTutorialUrl(string languageCode)
    {
        return LanguageCodeMatcher.IsChinese(languageCode)
            ? ChineseTutorialUrl
            : EnglishTutorialUrl;
    }

    internal static void OpenTutorial()
    {
        Application.OpenURL(ResolveTutorialUrl(L.CurrentLanguageCode));
    }
}
