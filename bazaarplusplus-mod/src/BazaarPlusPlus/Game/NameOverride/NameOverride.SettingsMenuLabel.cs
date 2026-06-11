#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.NameOverride;

internal static class NameOverrideSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Anonymous Mode",
        "匿名模式",
        "Anonymer Modus",
        "Modo anonimo",
        "익명 모드",
        "Modalita anonima"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
