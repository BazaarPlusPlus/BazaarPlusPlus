#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The upgrade preview on a mobile platform (ADR-0024): a touch screen has no Shift key, so, as
/// on the Android host, each item tooltip has its own upgrade toggle
/// (<see cref="TouchGameplayPreviews"/>) and the settings switch chooses where a new tooltip
/// starts. <c>BppHotkeyService.IsActive(HoldUpgradePreview)</c> reads the current tooltip's
/// toggle.
/// </summary>
internal static class TouchUpgradePreview
{
    private static readonly LocalizedTextSet Label = new("Upgrade preview", "升级预览", "升級預覽");

    private static ISetting<bool>? _setting;

    private static object? _card;
    private static bool _selected;

    /// <summary>The current tooltip's toggle, or null when the host is not on a mobile platform.</summary>
    internal static bool? Active => _setting == null ? null : _selected;

    /// <summary>Main thread, before a card shows its tooltip: a new card starts at the switch.</summary>
    internal static void BeginTooltip(object? card)
    {
        if (_setting == null || ReferenceEquals(card, _card))
            return;
        _card = card;
        _selected = _setting.Value;
    }

    /// <summary>The current tooltip's touch toggle.</summary>
    internal static bool Toggle() => _selected = !_selected;

    internal static void Bind(IBppSettingsStore store) =>
        _setting = store.Bind(
            "Touch",
            "UpgradePreview",
            false,
            "Mobile platforms only: whether item tooltips start with the next tier's values. Each tooltip also offers a touch toggle."
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
