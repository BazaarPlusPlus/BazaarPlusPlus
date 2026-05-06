#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.LegendaryPosition;

internal static class LegendaryPositionSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Legendary Position",
        "传奇名次显示",
        "傳奇名次顯示",
        "傳奇名次顯示"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
