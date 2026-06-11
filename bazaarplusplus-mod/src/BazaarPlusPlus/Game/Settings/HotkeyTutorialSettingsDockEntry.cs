#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

internal sealed class HotkeyTutorialSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.HotkeyTutorial;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "HotkeyTutorial",
            HotkeyTutorialSettingsMenuLabel.Resolve,
            HotkeyTutorialSettingsMenuLabel.ResolveOpenStatus,
            isActive: () => true,
            HotkeyTutorialLinks.OpenTutorial,
            collapseAfterActivate: true
        );
}
