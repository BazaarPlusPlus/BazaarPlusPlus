#nullable enable
using BazaarPlusPlus.Game.Settings;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal static class CombatStatusBarSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.CombatStatusBar,
            "CombatStatusBar",
            CombatStatusBarSettingsMenuLabel.Resolve,
            _ => CombatStatusBarFeature.GetEnabledSettingValue(),
            (_, enabled) => CombatStatusBarFeature.SetEnabledSettingValue(enabled)
        );
}
