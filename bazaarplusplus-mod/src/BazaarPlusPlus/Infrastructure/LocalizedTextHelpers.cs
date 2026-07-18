#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Infrastructure;

internal static class LocalizedTextHelpers
{
    public static string Resolve(LocalizedTextSet set) => L.Resolve(set);

    public static string FormatSimple(
        string english,
        string chineseMainland,
        string? chineseTraditional
    )
    {
        var languageCode = L.CurrentLanguageCode;
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return ChineseScriptConverter.Convert(
                chineseMainland,
                chineseTraditional,
                L.CurrentMode
            );
        }

        return english;
    }
}
