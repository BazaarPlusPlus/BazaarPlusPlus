using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Mobile.Previews;

/// <summary>
/// <c>[Touch] UpgradePreview</c>, the settings switch that stands in for the desktop's Shift
/// hotkey: where each item tooltip's own upgrade switch (<see cref="TouchPreviewSwitches"/>)
/// starts. Only the Android host binds it, so the desktop's config never holds it. No Unity
/// type, so a host test registers the row.
/// </summary>
internal static class TouchUpgradePreviewSetting
{
    /// <summary>The settings row's label and the tooltip switch's.</summary>
    public static readonly LocalizedTextSet Label = new("Upgrade preview", "升级预览", "升級預覽");

    public static ISetting<bool> Bind(IBppSettingsStore store) =>
        (store ?? throw new ArgumentNullException(nameof(store))).Bind(
            "Touch",
            "UpgradePreview",
            false,
            "Whether item tooltips start with the next tier's values. Each tooltip also offers a touch toggle."
        );

    /// <param name="setting">The switch <see cref="Bind"/> returned.</param>
    /// <param name="onChanged">What the host does with a new value beyond writing it.</param>
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
