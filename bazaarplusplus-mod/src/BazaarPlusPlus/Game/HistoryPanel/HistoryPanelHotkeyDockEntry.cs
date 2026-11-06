#nullable enable
using BazaarPlusPlus.Game.Input;

namespace BazaarPlusPlus.Game.HistoryPanel;

/// <summary>
/// The desktop's History row: Core's <see cref="HistoryPanelSettingsDockEntry"/> labelled with
/// the rebindable History hotkey, offered outside combat, opening the desktop panel.
/// </summary>
internal static class HistoryPanelHotkeyDockEntry
{
    internal static HistoryPanelSettingsDockEntry Create() =>
        Create(() => BppHotkeyService.GetBindingDisplay(BppHotkeyActionId.ToggleHistoryPanel));

    internal static HistoryPanelSettingsDockEntry Create(Func<string> resolveHotkeyDisplay) =>
        new(resolveHotkeyDisplay, () => !TheBazaar.Data.IsInCombat, HistoryPanel.OpenFromDockEntry);
}
