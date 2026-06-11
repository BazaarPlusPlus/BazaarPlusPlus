#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Combat Status Bar",
        "战斗状态",
        "Kampfstatusleiste",
        "Barra de status do combate",
        "전투 상태 바",
        "Barra stato combattimento"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
