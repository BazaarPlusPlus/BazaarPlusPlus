#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new("Combat Status", "战斗状态", "戰鬥狀態");

    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.CombatStatusBar,
            "CombatStatusBar",
            languageCode => Labels.Resolve(languageCode, L.CurrentMode),
            config => config.EnableCombatStatusBarConfig.Value,
            (config, enabled) => config.EnableCombatStatusBarConfig.Value = enabled,
            onChanged: CombatStatusBar.OnVisibilitySettingChanged
        );
}
