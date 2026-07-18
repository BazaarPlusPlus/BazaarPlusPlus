#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static class HistoryPanelRunHeroFilter
{
    public static bool Matches(string? selectedHero, HistoryRunRecord run)
    {
        return string.IsNullOrEmpty(selectedHero)
            || string.Equals(run.Hero, selectedHero, StringComparison.OrdinalIgnoreCase);
    }
}
