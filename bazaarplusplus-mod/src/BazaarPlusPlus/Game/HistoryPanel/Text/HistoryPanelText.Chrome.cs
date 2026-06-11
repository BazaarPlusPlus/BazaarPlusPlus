#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

internal static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet TitleText = new(
        "Game History",
        "对局历史",
        "對局歷史",
        "對局歷史"
    );

    private static readonly LocalizedTextSet SubtitleText = new(
        "Supported by the BazaarPlusPlus community.",
        "由 BazaarPlusPlus 玩家社区支持。",
        "由 BazaarPlusPlus 玩家社群支持。",
        "由 BazaarPlusPlus 玩家社群支持。"
    );

    private static readonly LocalizedTextSet RunsTabText = new("Runs", "对局", "對局", "對局");

    private static readonly LocalizedTextSet GhostTabText = new("Ghost", "幽灵", "幽靈", "幽靈");

    private static readonly LocalizedTextSet BattlesText = new("Battles", "战斗", "戰鬥", "戰鬥");

    private static readonly LocalizedTextSet CloseText = new("Close", "关闭", "關閉", "關閉");

    private static readonly LocalizedTextSet ReplayText = new("Replay", "回放", "重播", "重播");

    private static readonly LocalizedTextSet RecordAndReplayText = new(
        "Record",
        "录制",
        "錄製",
        "錄製"
    );

    private static readonly LocalizedTextSet ReplayUnavailableText = new("Unavailable", "不可用");

    private static readonly LocalizedTextSet ReplayDisabledInRunText = new("In Run", "对局中禁用");

    private static readonly LocalizedTextSet DownloadReplayText = new(
        "Download Replay",
        "下载回放",
        "下載重播",
        "下載重播"
    );

    private static readonly LocalizedTextSet DeleteText = new("Delete", "删除");

    private static readonly LocalizedTextSet DeleteConfirmText = new("Sure?", "确认？");

    private static readonly LocalizedTextSet WorkingText = new("Working...", "处理中...");


    internal static string Title() => Resolve(TitleText);

    internal static string Subtitle() => Resolve(SubtitleText);

    internal static string RunsTab() => Resolve(RunsTabText);

    internal static string GhostTab() => Resolve(GhostTabText);

    internal static string Battles() => Resolve(BattlesText);

    internal static string Close() => Resolve(CloseText);

    internal static string Replay() => Resolve(ReplayText);

    internal static string RecordAndReplay() => Resolve(RecordAndReplayText);

    internal static string ReplayUnavailable() => Resolve(ReplayUnavailableText);

    internal static string ReplayDisabledInRun() => Resolve(ReplayDisabledInRunText);

    internal static string DownloadReplay() => Resolve(DownloadReplayText);

    internal static string Delete() => Resolve(DeleteText);

    internal static string DeleteConfirm() => Resolve(DeleteConfirmText);

    internal static string Working() => Resolve(WorkingText);

    internal static string CountGhost(int count) => FormatCount(count, GhostTab());

    internal static string CountRuns(int count) => FormatCount(count, RunsTab());

    internal static string CountBattles(int count) => FormatCount(count, Battles());
}
