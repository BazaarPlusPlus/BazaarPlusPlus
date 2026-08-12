#nullable enable

using System.Globalization;
using BazaarPlusPlus.Game.LiveBuildPanel.Data;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet CorpusCardTitleText = new(
        "Ten-Win Build Data",
        "十胜阵容数据",
        "十勝陣容資料"
    );
    private static readonly LocalizedTextSet RefreshFinalBuildsText = new(
        "Pull Builds",
        "拉取阵容",
        "拉取陣容"
    );
    private static readonly LocalizedTextSet WorkingText = new("Working...", "处理中...");
    private static readonly LocalizedTextSet RefreshingFinalBuildsText = new(
        "Pulling ten-win builds...",
        "正在拉取十胜阵容...",
        "正在拉取十勝陣容..."
    );
    private static readonly LocalizedTextSet CorpusEmptyText = new(
        "No build data yet. Pull to load.",
        "尚未加载阵容数据，点击拉取。",
        "尚未載入陣容資料，點擊拉取。"
    );
    private static readonly LocalizedTextSet CorpusDataTimeLabelText = new(
        "data",
        "数据时间",
        "資料時間"
    );
    private static readonly LocalizedTextSet CorpusBuildCountUnitText = new(
        "builds",
        "套阵容",
        "套陣容"
    );
    private static readonly LocalizedTextSet CorpusHeroCountUnitText = new(
        "heroes",
        "位英雄",
        "位英雄"
    );

    public static string CorpusCardTitle() => L.Resolve(CorpusCardTitleText);

    public static string RefreshFinalBuilds() => L.Resolve(RefreshFinalBuildsText);

    public static string Working() => L.Resolve(WorkingText);

    public static string RefreshingFinalBuilds() => L.Resolve(RefreshingFinalBuildsText);

    public static string CorpusEmpty() => L.Resolve(CorpusEmptyText);

    // The corpus dashboard's freshness line: a localized relative-updated phrase plus the build
    // total. The absolute timestamp and per-hero breakdown stay on the tooltip (CorpusSummaryTooltip).
    public static string CorpusFreshnessLine(TenWinCorpusSummary summary, DateTimeOffset nowUtc) =>
        $"{RelativeUpdated(summary.GeneratedAtUtc, nowUtc)} · "
        + $"{summary.BuildCount.ToString("N0", CultureInfo.CurrentCulture)} "
        + L.Resolve(CorpusBuildCountUnitText);

    private static string RelativeUpdated(DateTimeOffset? generatedAtUtc, DateTimeOffset nowUtc)
    {
        if (!generatedAtUtc.HasValue)
            return L.Resolve(new LocalizedTextSet("updated —", "更新时间未知", "更新時間未知"));

        var delta = nowUtc - generatedAtUtc.Value;
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;

        if (delta.TotalHours < 1)
        {
            var minutes = Math.Max(1, (int)delta.TotalMinutes);
            return L.Resolve(
                new LocalizedTextSet(
                    $"updated {minutes}m ago",
                    $"更新于 {minutes} 分钟前",
                    $"更新於 {minutes} 分鐘前"
                )
            );
        }

        if (delta.TotalDays < 1)
        {
            var hours = (int)delta.TotalHours;
            return L.Resolve(
                new LocalizedTextSet(
                    $"updated {hours}h ago",
                    $"更新于 {hours} 小时前",
                    $"更新於 {hours} 小時前"
                )
            );
        }

        if (delta.TotalDays < 7)
        {
            var days = (int)delta.TotalDays;
            return L.Resolve(
                new LocalizedTextSet(
                    $"updated {days}d ago",
                    $"更新于 {days} 天前",
                    $"更新於 {days} 天前"
                )
            );
        }

        var weeks = (int)(delta.TotalDays / 7);
        return L.Resolve(
            new LocalizedTextSet(
                $"updated {weeks}w ago",
                $"更新于 {weeks} 周前",
                $"更新於 {weeks} 週前"
            )
        );
    }

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
                $"拉取十勝陣容失敗：{details}"
            )
        );
}
