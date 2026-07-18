#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Supporters;

internal static class FixedSupporterListSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new("Stream Mode", "直播模式");

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
