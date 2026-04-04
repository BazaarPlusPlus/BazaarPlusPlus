#nullable enable
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new(
        "Combat Status Bar",
        "战斗状态栏",
        "Kampfstatusleiste",
        "Barra de status do combate",
        "전투 상태 바",
        "Barra stato combattimento"
    );

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode);
    }
}
