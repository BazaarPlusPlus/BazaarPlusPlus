#nullable enable

using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static string Resolve(LocalizedTextSet set) => LocalizedTextHelpers.Resolve(set);

    private static string FormatCount(int count, string noun)
    {
        var languageCode = L.CurrentLanguageCode;
        if (LanguageCodeMatcher.IsChinese(languageCode))
            return $"{noun} {count}";

        return $"{count} {noun}";
    }

    private static string FormatSimple(string english, string chineseMainland)
    {
        return FormatSimple(english, chineseMainland, null, null);
    }

    private static string FormatSimple(
        string english,
        string chineseMainland,
        string? chineseTaiwan,
        string? chineseHongKong
    )
    {
        return LocalizedTextHelpers.FormatSimple(
            english,
            chineseMainland,
            chineseTaiwan,
            chineseHongKong
        );
    }

    private static string ResolveChinese(
        string chineseMainland,
        string? chineseTaiwan,
        string? chineseHongKong
    )
    {
        return ChineseScriptConverter.Convert(
            chineseMainland,
            chineseTaiwan,
            chineseHongKong,
            L.CurrentMode
        );
    }

    private static string Pluralize(int count, string singular, string plural)
    {
        return count == 1 ? singular : plural;
    }
}
