#nullable enable
using BazaarPlusPlus.Game.Input;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelHotkeyDockEntry
{
    internal static HistoryPanelSettingsDockEntry Create() =>
        Create(() => BppHotkeyService.GetBindingDisplay(BppHotkeyActionId.ToggleHistoryPanel));

    internal static HistoryPanelSettingsDockEntry Create(Func<string> resolveHotkeyDisplay) =>
        new(resolveHotkeyDisplay, () => !TheBazaar.Data.IsInCombat, HistoryPanel.OpenFromDockEntry);
}
