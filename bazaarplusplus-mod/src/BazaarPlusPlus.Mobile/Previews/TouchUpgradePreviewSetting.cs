using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Mobile.Previews;

internal static class TouchUpgradePreviewSetting
{
    public static readonly LocalizedTextSet Label = new("Upgrade preview", "升级预览", "升級預覽");

    public static ISetting<bool> Bind(IBppSettingsStore store) =>
        (store ?? throw new ArgumentNullException(nameof(store))).Bind(
            "Touch",
            "UpgradePreview",
            false,
            "Whether item tooltips start with the next tier's values. Each tooltip also offers a touch toggle."
        );

    public static CyclingSettingsDockEntry<bool> DockEntry(
        ISetting<bool> setting,
        Action<bool>? onChanged = null
    )
    {
        if (setting == null)
            throw new ArgumentNullException(nameof(setting));
        return CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.UpgradePreviewSwitch,
            "UpgradePreview",
            languageCode => Label.Resolve(languageCode, L.CurrentMode),
            _ => setting.Value,
            (_, enabled) => setting.Value = enabled,
            onChanged
        );
    }
}
