using System;

namespace BazaarPlusPlus.Game.Settings;

internal static class SimplifiedChineseLanguage
{
    internal static bool Matches(string languageCode)
    {
        return LanguageCodeMatcher.IsSimplifiedChinese(languageCode);
    }
}
