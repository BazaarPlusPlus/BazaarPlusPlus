#nullable enable

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelAccessPolicy
{
    internal static bool CanOpen(bool isInGameRun, bool communityContributionEnabled)
    {
        return !isInGameRun && communityContributionEnabled;
    }
}
