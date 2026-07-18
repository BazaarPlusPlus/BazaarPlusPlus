#nullable enable

using System.Globalization;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet TenWinLabelText = new("10-win", "十胜", "十勝");
    private static readonly LocalizedTextSet MatchRateLabelText = new(
        "Ten-win rate",
        "十胜率",
        "十勝率"
    );
    private static readonly LocalizedTextSet MatchSampleLabelText = new(
        "10-win runs",
        "十胜场次",
        "十勝場次"
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

    public static string RecommendationCount(int index, int count) =>
        count <= 0 ? NoRecommendation() : $"{index + 1}/{count}";

    public static string MatchRateLabel() => L.Resolve(MatchRateLabelText);

    public static string MatchSampleLabel() => L.Resolve(MatchSampleLabelText);

    public static string MatchFinalDayLabel() => L.Resolve(MatchFinalDayLabelText);

    public static string MatchMatchedLabel() => L.Resolve(MatchMatchedLabelText);

    // Ten-win rate from basis points (2667 -> 26.67%); em dash when the analyzer omitted it.
    public static string MatchRateValue(int? tenWinRateBps) =>
        tenWinRateBps.HasValue
            ? $"{(tenWinRateBps.Value / 100.0).ToString("0.##", CultureInfo.InvariantCulture)}%"
            : L.Resolve(MissingValueText);

    public static string MatchSampleValue(int tenWinRunCount) =>
        tenWinRunCount.ToString("N0", CultureInfo.CurrentCulture);

    public static string MatchFinalDayValue(int? p75FinalDay) =>
        p75FinalDay.HasValue ? $"D{p75FinalDay.Value}" : L.Resolve(MissingValueText);

    public static string MatchMatchedValue(int matched, int candidates) =>
        $"{matched} / {candidates}";
}
