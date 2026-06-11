#nullable enable

using System.Collections.Generic;
using System.Globalization;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet CandidateCountText = new(
        "Candidates",
        "候选",
        "候選",
        "候選"
    );
    private static readonly LocalizedTextSet TenWinLabelText = new(
        "10-win",
        "十胜",
        "十勝",
        "十勝"
    );

    public static string CandidateCount(int count) => $"{L.Resolve(CandidateCountText)} {count}";

    public static string RecommendationCount(int index, int count) =>
        count <= 0 ? NoRecommendation() : $"{index + 1}/{count}";

    // Compact ten-win evidence for the status rail: run count, success rate (basis points -> percent),
    // and p75 final day. Replaces the legacy free-text "source" suffix.
    public static string RecommendationEvidence(
        int tenWinRunCount,
        int? tenWinRateBps,
        int? p75FinalDay
    )
    {
        var parts = new List<string> { $"{L.Resolve(TenWinLabelText)} {tenWinRunCount}" };
        if (tenWinRateBps.HasValue)
            parts.Add(
                $"{(tenWinRateBps.Value / 100.0).ToString("0.##", CultureInfo.InvariantCulture)}%"
            );
        if (p75FinalDay.HasValue)
            parts.Add($"D{p75FinalDay.Value}");
        return string.Join(" · ", parts);
    }
}
