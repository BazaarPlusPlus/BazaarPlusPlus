#nullable enable
using BazaarPlusPlus.Infrastructure.UiTokens;
using UnityEngine;

namespace BazaarPlusPlus.Game.HistoryPanel.Ui;

internal sealed partial class HistoryPanelUiToolkitView
{
    private static (Color Background, Color Text) GetRankBadgePalette(string rank)
    {
        return rank switch
        {
            "Bronze" => (Colors.RankBronzeBackground, Colors.RankBronzeText),
            "Silver" => (Colors.RankSilverBackground, Colors.RankSilverText),
            "Gold" => (Colors.RankGoldBackground, Colors.RankGoldText),
            "Diamond" => (Colors.RankDiamondBackground, Colors.RankDiamondText),
            _ => (Colors.RankDefaultBackground, Colors.HistoryProgressText),
        };
    }

    private static (Color Background, Color Border) GetOutcomePalette(RunOutcomeTier tier)
    {
        return tier switch
        {
            RunOutcomeTier.Diamond => (
                Colors.OutcomeDiamondBackground,
                Colors.OutcomeDiamondBorder
            ),
            RunOutcomeTier.Gold => (Colors.OutcomeGoldBackground, Colors.OutcomeGoldBorder),
            RunOutcomeTier.Silver => (Colors.OutcomeSilverBackground, Colors.OutcomeSilverBorder),
            RunOutcomeTier.Bronze => (Colors.OutcomeBronzeBackground, Colors.OutcomeBronzeBorder),
            _ => (Colors.OutcomeMisfortuneBackground, Colors.OutcomeMisfortuneBorder),
        };
    }

    private static Color GetBattleRowBackground(
        bool selected,
        bool isEliminated,
        bool isWin,
        bool isLoss
    )
    {
        return selected
            ? isEliminated
                ? Colors.BattleRowEliminatedSelectedBackground
                : isWin
                    ? Colors.BattleRowWinSelectedBackground
                    : isLoss
                        ? Colors.BattleRowLossSelectedBackground
                        : Colors.BattleRowNeutralSelectedBackground
            : isEliminated
                ? Colors.BattleRowEliminatedBackground
                : isWin
                    ? Colors.BattleRowWinBackground
                    : isLoss
                        ? Colors.BattleRowLossBackground
                        : Colors.BattleRowNeutralBackground;
    }

    private static Color GetBattleAccent(bool isEliminated, bool isWin, bool isLoss)
    {
        return isEliminated ? Colors.BattleRowEliminatedAccent
            : isWin ? Colors.BattleRowWinAccent
            : isLoss ? Colors.BattleRowLossAccent
            : Colors.BattleRowNeutralAccent;
    }

    private static Color GetBattleBorder(bool isEliminated, bool isWin, bool isLoss)
    {
        return isEliminated ? Colors.BattleRowEliminatedBorder
            : isWin ? Colors.BattleRowWinBorder
            : isLoss ? Colors.BattleRowLossBorder
            : Colors.BattleRowNeutralBorder;
    }

    private static Color GetBattleDayBackground(bool isEliminated, bool isWin, bool isLoss)
    {
        return isEliminated ? Colors.BattleDayEliminatedBackground
            : isWin ? Colors.BattleDayWinBackground
            : isLoss ? Colors.BattleDayLossBackground
            : Colors.BattleDayNeutralBackground;
    }
}
