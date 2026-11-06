#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CombatStatusBar;

/// <summary>
/// The Combat Status row, <c>[CombatStatusBar] Enabled</c>: the desktop's combat status bar and
/// the Mobile UI's combat clock on Android read it.
/// </summary>
public static class CombatStatusBarSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new("Combat Status", "战斗状态", "戰鬥狀態");

    public static CyclingSettingsDockEntry<bool> Create(Action<bool>? onChanged = null) =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.CombatStatusBar,
            "CombatStatusBar",
            languageCode => Labels.Resolve(languageCode, L.CurrentMode),
            config => config.EnableCombatStatusBarConfig.Value,
            (config, enabled) => config.EnableCombatStatusBarConfig.Value = enabled,
            onChanged
        );
}
