#nullable enable

using System.Globalization;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

public static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet MatchRateLabelText = new(
        "Ten-win rate",
        "十胜率",
        "十勝率"
    );
    private static readonly LocalizedTextSet MatchSampleLabelText = new(
        "10-win / total runs",
        "十胜 / 总局数",
        "十勝 / 總局數"
    );
    private static readonly LocalizedTextSet MatchFinalDayLabelText = new(
        "Final day · p75",
        "终局天数 · p75",
        "終局天數 · p75"
    );
    private static readonly LocalizedTextSet MatchMatchedLabelText = new(
        "Matched cards",
        "命中候选",
        "命中候選"
    );
    private static readonly LocalizedTextSet MissingValueText = new("—", "—", "—");

    /// <summary>
    /// The evidence cells both hosts show for the browsed recommendation, in display order: the
    /// ten-win rate, the ten-win and completed run counts, the final day, and the candidates it
    /// holds. No recommendation leaves the cells with missing values.
    /// </summary>
    public static IReadOnlyList<LiveBuildMetric> MatchMetrics(
        BuildRecommendation? match,
        int candidateCount
    ) =>
        [
            new(L.Resolve(MatchRateLabelText), MatchRateValue(match?.TenWinRateBps)),
            new(
                L.Resolve(MatchSampleLabelText),
                MatchSampleValue(match?.TenWinRunCount ?? 0, match?.CompletedRunCount ?? 0)
            ),
            new(L.Resolve(MatchFinalDayLabelText), MatchFinalDayValue(match?.P75TenWinFinalDay)),
            new(
                L.Resolve(MatchMatchedLabelText),
                $"{match?.MatchedCardCount ?? 0} / {candidateCount}"
            ),
        ];

    // Ten-win rate from basis points (2667 -> 26.67%); em dash when the analyzer omitted it.
    private static string MatchRateValue(int? tenWinRateBps) =>
        tenWinRateBps.HasValue
            ? $"{(tenWinRateBps.Value / 100.0).ToString("0.##", CultureInfo.InvariantCulture)}%"
            : L.Resolve(MissingValueText);

    private static string MatchSampleValue(int tenWinRunCount, int completedRunCount) =>
        $"{tenWinRunCount.ToString("N0", CultureInfo.CurrentCulture)} / {completedRunCount.ToString("N0", CultureInfo.CurrentCulture)}";

    private static string MatchFinalDayValue(int? p75FinalDay) =>
        p75FinalDay.HasValue ? $"D{p75FinalDay.Value}" : L.Resolve(MissingValueText);
}
