#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelSettingsDockEntry : ISettingsDockEntry
{
    private readonly Func<string> _resolveHotkeyDisplay;

    internal HistoryPanelSettingsDockEntry()
        : this(() => BppHotkeyService.GetBindingDisplay(BppHotkeyActionId.ToggleHistoryPanel)) { }

    internal HistoryPanelSettingsDockEntry(Func<string> resolveHotkeyDisplay)
    {
        _resolveHotkeyDisplay =
            resolveHotkeyDisplay ?? throw new ArgumentNullException(nameof(resolveHotkeyDisplay));
    }

    public int Order => BppSettingsDockOrder.GameHistory;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "GameHistory",
            languageCode =>
                HistoryPanelSettingsMenuLabel.Resolve(languageCode, _resolveHotkeyDisplay()),
            IsHistoryPanelActionable,
            HistoryPanel.OpenFromDockEntry,
            collapseAfterActivate: true
        );

    private static bool IsHistoryPanelActionable()
    {
        return !TheBazaar.Data.IsInCombat;
    }
}
