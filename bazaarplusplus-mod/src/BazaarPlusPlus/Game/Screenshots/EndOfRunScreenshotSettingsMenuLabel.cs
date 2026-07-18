#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Screenshots;

internal static class EndOfRunScreenshotSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "End-of-run Screenshot",
        "终局截图",
        "終局截圖"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
