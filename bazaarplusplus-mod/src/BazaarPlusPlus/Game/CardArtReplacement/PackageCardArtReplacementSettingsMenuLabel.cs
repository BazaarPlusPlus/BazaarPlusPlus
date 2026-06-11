#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CardArtReplacement;

internal static class PackageCardArtReplacementSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new("Package Swap", "掉包快递");

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
