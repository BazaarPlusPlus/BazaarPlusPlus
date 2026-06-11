#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatStatusBarSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.CombatStatusBar;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "CombatStatusBar",
            CombatStatusBarSettingsMenuLabel.Resolve,
            new CombatStatusBarSettingsMenuBridge(
                CombatStatusBarFeature.GetEnabledSettingValue,
                CombatStatusBarFeature.SetEnabledSettingValue
            )
        );
}
