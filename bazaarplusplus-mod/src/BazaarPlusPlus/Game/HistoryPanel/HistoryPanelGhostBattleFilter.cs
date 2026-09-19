#nullable enable
using BazaarPlusPlus.Game.HistoryPanel.Data;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal enum HistoryPanelGhostBattleOutcome
{
    Unknown,
    Won,
    Lost,
}

internal static class HistoryPanelGhostBattleFilter
{
    internal static HistoryPanelGhostBattleOutcome ResolveOutcome(HistoryBattleRecord battle)
    {
        if (
            string.Equals(
                battle.WinnerCombatantId?.Trim(),
                "Player",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return HistoryPanelGhostBattleOutcome.Won;

        if (
            string.Equals(
                battle.WinnerCombatantId?.Trim(),
                "Opponent",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return HistoryPanelGhostBattleOutcome.Lost;

        var result = battle.Result?.Trim();
        if (
            string.Equals(result, "Win", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result, "Won", StringComparison.OrdinalIgnoreCase)
        )
            return HistoryPanelGhostBattleOutcome.Won;

        if (
            string.Equals(result, "Loss", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result, "Lost", StringComparison.OrdinalIgnoreCase)
        )
            return HistoryPanelGhostBattleOutcome.Lost;

        return HistoryPanelGhostBattleOutcome.Unknown;
    }
}
