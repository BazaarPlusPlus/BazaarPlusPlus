#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

public static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet TitleText = new(
        "Game History",
        "对局历史",
        "對局歷史"
    );

    private static readonly LocalizedTextSet SubtitleText = new(
        "Supported by the BazaarPlusPlus community.",
        "由 BazaarPlusPlus 玩家社区支持。",
        "由 BazaarPlusPlus 玩家社群支持。"
    );

    private static readonly LocalizedTextSet RunsTabText = new("Runs", "对局", "對局");

    private static readonly LocalizedTextSet GhostTabText = new("Ghost", "幽灵", "幽靈");

    private static readonly LocalizedTextSet CloseText = new("Close", "关闭", "關閉");

    private static readonly LocalizedTextSet ReplayText = new("Replay", "回放", "重播");

    private static readonly LocalizedTextSet RecordAndReplayText = new("Record", "录制", "錄製");

    private static readonly LocalizedTextSet ReplayUnavailableText = new("Unavailable", "不可用");

    private static readonly LocalizedTextSet ReplayDisabledInRunText = new("In Run", "对局中禁用");

    private static readonly LocalizedTextSet DownloadReplayText = new(
        "Download Replay",
        "下载回放",
        "下載重播"
    );

    private static readonly LocalizedTextSet DeleteText = new("Delete", "删除");

    private static readonly LocalizedTextSet DeleteConfirmText = new("Sure?", "确认？");

    private static readonly LocalizedTextSet WorkingText = new("Working...", "处理中...");

    public static string Title() => Resolve(TitleText);

    public static string Subtitle() => Resolve(SubtitleText);

    public static string RunsTab() => Resolve(RunsTabText);

    public static string GhostTab() => Resolve(GhostTabText);

    public static string Close() => Resolve(CloseText);

    public static string Replay() => Resolve(ReplayText);

    public static string RecordAndReplay() => Resolve(RecordAndReplayText);

    public static string ReplayUnavailable() => Resolve(ReplayUnavailableText);

    public static string ReplayDisabledInRun() => Resolve(ReplayDisabledInRunText);

    public static string DownloadReplay() => Resolve(DownloadReplayText);

    public static string Delete() => Resolve(DeleteText);

    public static string DeleteConfirm() => Resolve(DeleteConfirmText);

    public static string Working() => Resolve(WorkingText);

    public static string PageTotal(long total, bool ghost) =>
        ghost
            ? FormatSimple($"{total} battles total", $"共 {total} 场")
            : FormatSimple($"{total} runs total", $"共 {total} 局");

    public static string PagePosition(long first, long last, long total, bool ghost) =>
        ghost
            ? FormatSimple(
                $"Battles {first}–{last} · {total} total",
                $"第 {first}–{last} 场 · 共 {total} 场"
            )
            : FormatSimple(
                $"Runs {first}–{last} · {total} total",
                $"第 {first}–{last} 局 · 共 {total} 局"
            );
}
