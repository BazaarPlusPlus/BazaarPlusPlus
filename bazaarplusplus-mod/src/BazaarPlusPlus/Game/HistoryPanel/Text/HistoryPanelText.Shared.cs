#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static string Resolve(LocalizedTextSet set) => L.Resolve(set);

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
        return L.Resolve(new LocalizedTextSet(english, chineseMainland, chineseTraditional));
    }
}
