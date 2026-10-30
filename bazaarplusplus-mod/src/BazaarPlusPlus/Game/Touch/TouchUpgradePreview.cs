#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The upgrade preview on a mobile platform (ADR-0024): a touch screen has no Shift key, so a
/// settings switch stands in for it, as the Android host's <c>Android.UpgradePreview</c> switch.
/// While the switch is on, <c>BppHotkeyService.IsActive(HoldUpgradePreview)</c> is true.
/// </summary>
internal static class TouchUpgradePreview
{
    private static readonly LocalizedTextSet Label = new("Upgrade preview", "升级预览", "升級預覽");

    private static ISetting<bool>? _setting;

    /// <summary>The switch's state, or null when the host is not on a mobile platform.</summary>
    internal static bool? Active => _setting?.Value;

    internal static void Bind(IBppSettingsStore store) =>
        _setting = store.Bind(
            "Touch",
            "UpgradePreview",
            false,
            "Mobile platforms only: whether item tooltips show the next tier's values, in place of holding Shift."
        );

    internal static CyclingSettingsDockEntry<bool> CreateDockEntry() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.UpgradePreviewSwitch,
            "UpgradePreview",
            languageCode => Label.Resolve(languageCode, L.CurrentMode),
            _ => _setting?.Value == true,
            (_, enabled) =>
            {
                if (_setting != null)
                    _setting.Value = enabled;
            },
            _ => { }
        );
}
