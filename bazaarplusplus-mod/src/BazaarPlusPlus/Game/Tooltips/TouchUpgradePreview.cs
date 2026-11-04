#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Tooltips;

/// <summary>
/// The upgrade preview on a mobile platform (ADR-0024): a touch screen has no Shift key, so, as
/// on the Android host, each item tooltip has its own upgrade switch (the Mobile UI's
/// <c>TouchPreviewSwitches</c>, through <see cref="TouchTooltipPreviewHost"/>) and the settings
/// switch, the Mobile UI's <c>[Touch] UpgradePreview</c>, chooses where a new tooltip starts.
/// <c>BppHotkeyService.IsActive(HoldUpgradePreview)</c> reads the current tooltip's switch. Host
/// state the desktop's tooltip patches read, so it stays in the host, as Android's
/// <c>AndroidUpgradePreview</c> does. While the play blocks the upgrade preview, no card
/// previews its upgrade.
/// </summary>
internal static class TouchUpgradePreview
{
    private static ISetting<bool>? _setting;
    private static Func<bool> _previewBlocked = static () => false;

    private static object? _card;
    private static bool _selected;

    /// <summary>The current tooltip's switch, or null when the host is not on a mobile platform.</summary>
    internal static bool? Active => _setting == null ? null : _selected && !_previewBlocked();

    /// <summary>Main thread, before a card shows its tooltip: a new card starts at the settings switch.</summary>
    internal static void BeginTooltip(object? card)
    {
        if (_setting == null)
            return;
        if (_previewBlocked())
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

    // setting: the settings switch. previewBlocked: whether the play blocks every card's upgrade
    // preview (the Mobile UI's TouchPlayState.PreviewsUpgrade, read through its port).
    internal static void Bind(ISetting<bool> setting, Func<bool> previewBlocked)
    {
        _setting = setting;
        _previewBlocked = previewBlocked;
    }
}
