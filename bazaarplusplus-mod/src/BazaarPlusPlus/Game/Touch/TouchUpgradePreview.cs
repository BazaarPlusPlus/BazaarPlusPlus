#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The upgrade preview on a mobile platform (ADR-0024): a touch screen has no Shift key, so, as
/// on the Android host, each item tooltip has its own upgrade switch (the Mobile UI's
/// <c>TouchPreviewSwitches</c>, through <see cref="TouchTooltipPreviewHost"/>) and the settings
/// switch chooses where a new tooltip starts. <c>BppHotkeyService.IsActive(HoldUpgradePreview)</c>
/// reads the current tooltip's switch. Host state the desktop's tooltip patches read, so it stays
/// in the host, as Android's <c>AndroidUpgradePreview</c> does. As on the Android host, a replay's
/// card never previews its upgrade and offers no switch.
/// </summary>
internal static class TouchUpgradePreview
{
    private static readonly LocalizedTextSet Label = new("Upgrade preview", "升级预览", "升級預覽");

    private static ISetting<bool>? _setting;
    private static Func<bool> _showsReplay = static () => false;

    private static object? _card;
    private static bool _selected;

    /// <summary>The current tooltip's switch, or null when the host is not on a mobile platform.</summary>
    internal static bool? Active => _setting == null ? null : _selected && !_showsReplay();

    /// <summary>Whether the current tooltip may preview its upgrade: a mobile platform, not a replay.</summary>
    internal static bool Offered => _setting != null && !_showsReplay();

    /// <summary>Main thread, before a card shows its tooltip: a new card starts at the settings switch.</summary>
    internal static void BeginTooltip(object? card)
    {
        if (_setting == null)
            return;
        if (_showsReplay())
        {
            End();
            return;
        }
        if (ReferenceEquals(card, _card))
            return;
        _card = card;
        _selected = _setting.Value;
    }

    /// <summary>Whether <paramref name="card"/>'s tooltip is the current one and shows its upgrade.</summary>
    internal static bool Holds(object card) => Active == true && ReferenceEquals(card, _card);

    /// <summary>The current tooltip's touch switch.</summary>
    internal static bool Toggle() => _selected = !_selected;

    /// <summary>The tooltip closed: the card's next tooltip starts at the settings switch again.</summary>
    internal static void End()
    {
        _card = null;
        _selected = false;
    }

    // showsReplay: whether the game shows a replay, whose cards preview no upgrade.
    internal static void Bind(IBppSettingsStore store, Func<bool> showsReplay)
    {
        _showsReplay = showsReplay;
        _setting = store.Bind(
            "Touch",
            "UpgradePreview",
            false,
            "Mobile platforms only: whether item tooltips start with the next tier's values. Each tooltip also offers a touch toggle."
        );
    }

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
