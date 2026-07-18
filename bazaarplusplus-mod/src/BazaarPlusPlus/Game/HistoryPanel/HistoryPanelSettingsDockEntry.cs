#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal sealed class HistoryPanelSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.GameHistory;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "GameHistory",
            HistoryPanelSettingsMenuLabel.Resolve,
            IsHistoryPanelActionable,
            HistoryPanel.OpenFromDockEntry,
            collapseAfterActivate: true
        );

    private static bool IsHistoryPanelActionable()
    {
        return !TheBazaar.Data.IsInCombat;
    }
}
