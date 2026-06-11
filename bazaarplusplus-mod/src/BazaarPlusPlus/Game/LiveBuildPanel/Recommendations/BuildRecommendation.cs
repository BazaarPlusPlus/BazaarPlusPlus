#nullable enable
using BazaarPlusPlus.GameInterop.ItemBoardPreview;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal sealed class BuildRecommendation
{
    public string ModeLabel { get; set; } = string.Empty;

    /// <summary>How many of the selected candidate cards this build contains.</summary>
    public int MatchedCardCount { get; set; }

    public int TenWinRunCount { get; set; }

    /// <summary>Ten-win rate in basis points (2667 == 26.67%), or null when unavailable.</summary>
    public int? TenWinRateBps { get; set; }

    /// <summary>p75 ten-win final day (plain day, not tenths), or null when unavailable.</summary>
    public int? P75TenWinFinalDay { get; set; }

    /// <summary>Analyzer-side integer rank score used for package selection and tie-breaking.</summary>
    public long Score { get; set; }

    public int ResultIndex { get; set; }

    public int ResultCount { get; set; }

    public BppItemBoard Board { get; set; } = BppItemBoard.Empty;
}
