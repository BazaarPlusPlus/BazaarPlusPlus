#nullable enable
using System;
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
    public static bool Matches(GhostBattleFilter filter, HistoryBattleRecord battle)
    {
        var outcome = ResolveOutcome(battle);
        return filter switch
        {
            GhostBattleFilter.IWon => outcome == HistoryPanelGhostBattleOutcome.Won,
            GhostBattleFilter.ILost => outcome == HistoryPanelGhostBattleOutcome.Lost,
            _ => true,
        };
    }

    public static HistoryPanelGhostBattleOutcome ResolveOutcomeForCompatibility(
        HistoryBattleRecord battle
    )
    {
        return ResolveOutcome(battle);
    }

    private static HistoryPanelGhostBattleOutcome ResolveOutcome(HistoryBattleRecord battle)
    {
        if (string.Equals(battle.WinnerCombatantId, "Player", StringComparison.OrdinalIgnoreCase))
            return HistoryPanelGhostBattleOutcome.Won;

        if (string.Equals(battle.WinnerCombatantId, "Opponent", StringComparison.OrdinalIgnoreCase))
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
