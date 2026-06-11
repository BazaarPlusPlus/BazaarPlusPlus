#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LegendaryPosition;

internal static class LegendaryPositionSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Legendary Position",
        "传奇名次",
        "傳奇名次",
        "傳奇名次"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
