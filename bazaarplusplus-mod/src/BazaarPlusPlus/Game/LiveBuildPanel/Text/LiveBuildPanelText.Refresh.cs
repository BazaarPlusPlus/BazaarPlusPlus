#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet CorpusCardTitleText = new(
        "Ten-Win Build Data",
        "十胜阵容数据",
        "十勝陣容資料",
        "十勝陣容資料"
    );
    private static readonly LocalizedTextSet RefreshFinalBuildsText = new(
        "Pull Builds",
        "拉取阵容",
        "拉取陣容",
        "拉取陣容"
    );
    private static readonly LocalizedTextSet WorkingText = new("Working...", "处理中...");
    private static readonly LocalizedTextSet RefreshingFinalBuildsText = new(
        "Pulling ten-win builds...",
        "正在拉取十胜阵容...",
        "正在拉取十勝陣容...",
        "正在拉取十勝陣容..."
    );
    private static readonly LocalizedTextSet CorpusEmptyText = new(
        "No build data yet. Pull to load.",
        "尚未加载阵容数据，点击拉取。",
        "尚未載入陣容資料，點擊拉取。",
        "尚未載入陣容資料，點擊拉取。"
    );
    private static readonly LocalizedTextSet CorpusDataTimeLabelText = new(
        "data",
        "数据时间",
        "資料時間",
        "資料時間"
    );
    private static readonly LocalizedTextSet CorpusBuildCountUnitText = new(
        "builds",
        "套阵容",
        "套陣容",
        "套陣容"
    );
    private static readonly LocalizedTextSet CorpusHeroCountUnitText = new(
        "heroes",
        "位英雄",
        "位英雄",
        "位英雄"
    );

    public static string CorpusCardTitle() => L.Resolve(CorpusCardTitleText);

    public static string RefreshFinalBuilds() => L.Resolve(RefreshFinalBuildsText);

    public static string Working() => L.Resolve(WorkingText);

    public static string RefreshingFinalBuilds() => L.Resolve(RefreshingFinalBuildsText);

    public static string CorpusEmpty() => L.Resolve(CorpusEmptyText);

    // High-value fields only (data time + totals); the per-hero breakdown lives in
    // CorpusSummaryTooltip so the corpus card body stays within its fixed two-line height.
    public static string CorpusSummaryLine(TenWinCorpusSummary summary) =>
        string.Join(" · ", CorpusSummaryParts(summary));

    public static string CorpusSummaryTooltip(TenWinCorpusSummary summary)
    {
        var parts = CorpusSummaryParts(summary);
        parts.AddRange(
            summary
                .HeroBuildCounts.Where(count => !string.IsNullOrWhiteSpace(count.Hero))
                .Select(count => $"{count.Hero} {count.BuildCount}")
        );
        return string.Join(" · ", parts);
    }

    private static List<string> CorpusSummaryParts(TenWinCorpusSummary summary)
    {
        var parts = new List<string>();
        if (summary.GeneratedAtUtc.HasValue)
        {
            var localTime = summary
                .GeneratedAtUtc.Value.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            parts.Add($"{L.Resolve(CorpusDataTimeLabelText)} {localTime}");
        }

        parts.Add($"{summary.BuildCount} {L.Resolve(CorpusBuildCountUnitText)}");
        parts.Add($"{summary.HeroCount} {L.Resolve(CorpusHeroCountUnitText)}");
        return parts;
    }

    public static string FinalBuildRefreshFailed(string details) =>
        L.Resolve(
            new LocalizedTextSet(
                $"Couldn't pull ten-win builds: {details}",
                $"拉取十胜阵容失败：{details}",
                $"拉取十勝陣容失敗：{details}",
                $"拉取十勝陣容失敗：{details}"
            )
        );
}
