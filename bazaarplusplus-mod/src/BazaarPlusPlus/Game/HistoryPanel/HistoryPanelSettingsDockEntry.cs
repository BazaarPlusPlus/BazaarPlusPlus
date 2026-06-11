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
            ResolveHistoryPanelStatus,
            IsHistoryPanelActionable,
            HistoryPanel.OpenFromDockEntry,
            collapseAfterActivate: true
        );

    private static string ResolveHistoryPanelStatus(string languageCode)
    {
        if (TheBazaar.Data.IsInCombat)
            return HistoryPanelSettingsMenuLabel.ResolveInRunStatus(languageCode);

        return HistoryPanel.IsVisible
            ? HistoryPanelSettingsMenuLabel.ResolveOpenStatus(languageCode)
            : HistoryPanelSettingsMenuLabel.ResolveViewStatus(languageCode);
    }

    private static bool IsHistoryPanelActionable()
    {
        return !TheBazaar.Data.IsInCombat;
    }
}
