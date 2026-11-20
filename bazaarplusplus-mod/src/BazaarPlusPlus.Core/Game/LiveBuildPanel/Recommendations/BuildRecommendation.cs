#nullable enable
using BazaarPlusPlus.Core.Cards.ItemBoards;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

public sealed class BuildRecommendation
{
    public int MatchedCardCount { get; set; }

    public int TenWinRunCount { get; set; }

    public int CompletedRunCount { get; set; }

    public int? TenWinRateBps { get; set; }

    // The analyzer sends this percentile as a rounded day, with no tenths.
    public int? P75TenWinFinalDay { get; set; }

    public long Score { get; set; }

    public BppItemBoard Board { get; set; } = BppItemBoard.Empty;
}
