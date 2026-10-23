#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// The settings row that opens the History panel and closes the settings dialog. Each host
/// supplies how it opens the panel and when it can: the desktop names its rebindable hotkey in
/// the label; a host without one (Android) passes no hotkey and shows the plain name.
/// </summary>
public sealed class HistoryPanelSettingsDockEntry : ISettingsDockEntry
{
    private readonly Func<string>? _resolveHotkeyDisplay;
    private readonly Func<bool> _isActionable;
    private readonly Action _open;

    public HistoryPanelSettingsDockEntry(
        Func<string>? resolveHotkeyDisplay,
        Func<bool> isActionable,
        Action open
    )
    {
        _resolveHotkeyDisplay = resolveHotkeyDisplay;
        _isActionable = isActionable ?? throw new ArgumentNullException(nameof(isActionable));
        _open = open ?? throw new ArgumentNullException(nameof(open));
    }

    public int Order => BppSettingsDockOrder.GameHistory;

    public BppSettingsDockDefinition Build(BppSettings config) =>
        new(
            "GameHistory",
            languageCode =>
                _resolveHotkeyDisplay == null
                    ? HistoryPanelSettingsMenuLabel.ResolvePlain(languageCode)
                    : HistoryPanelSettingsMenuLabel.Resolve(languageCode, _resolveHotkeyDisplay()),
            _isActionable,
            _open,
            collapseAfterActivate: true
        );
}
