#nullable enable

using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static string Resolve(LocalizedTextSet set) => LocalizedTextHelpers.Resolve(set);

    private static string FormatSimple(string english, string chineseMainland)
    {
        return FormatSimple(english, chineseMainland, null);
    }

    private static string FormatSimple(
        string english,
        string chineseMainland,
        string? chineseTraditional
    )
    {
        return LocalizedTextHelpers.FormatSimple(english, chineseMainland, chineseTraditional);
    }
}
