#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet TitleText = new(
        "Ten-Win Builds",
        "十胜阵容",
        "十勝陣容"
    );
    private static readonly LocalizedTextSet SubtitleText = new(
        "Choose live item candidates and browse matching ten-win builds.",
        "选择当前局内物品候选，并查看匹配的十胜阵容。",
        "選擇當前局內物品候選，並查看匹配的十勝陣容。"
    );
    private static readonly LocalizedTextSet FinalBuildRowText = new(
        "Ten-Win Build",
        "十胜推荐",
        "十勝推薦"
    );
    private static readonly LocalizedTextSet ShopRowText = new(
        "Shop Items",
        "商店物品",
        "商店物品"
    );
    private static readonly LocalizedTextSet BoardRowText = new(
        "Current Build",
        "当前阵容",
        "當前陣容"
    );
    private static readonly LocalizedTextSet StashRowText = new(
        "Stash Items",
        "箱子物品",
        "箱子物品"
    );
    private static readonly LocalizedTextSet CloseText = new("Close", "关闭", "關閉");

    public static string SelectionHint() =>
        L.Resolve(
            new LocalizedTextSet(
                "Click items to toggle candidates",
                "点击物品切换候选",
                "點擊物品切換候選"
            )
        );

    public static string CandidateTag() =>
        L.Resolve(new LocalizedTextSet("Candidate", "候选", "候選"));

    public static string MatchTag() => L.Resolve(new LocalizedTextSet("Match", "命中", "命中"));

    public static string CorpusLibrary() =>
        L.Resolve(new LocalizedTextSet("Build library", "阵容库", "陣容庫"));

    public static string BrowseBuilds() =>
        L.Resolve(new LocalizedTextSet("Browse builds", "切换阵容", "切換陣容"));

    public static string LoadingBoard() =>
        L.Resolve(new LocalizedTextSet("Loading items…", "正在加载物品…", "正在載入物品…"));

    public static string BoardFailed() =>
        L.Resolve(
            new LocalizedTextSet(
                "Items couldn't load. Reopen to retry.",
                "物品加载失败，请重新打开重试。",
                "物品載入失敗，請重新開啟重試。"
            )
        );

    public static string Title() => L.Resolve(TitleText);

    public static string Subtitle() => L.Resolve(SubtitleText);

    public static string FinalBuildRow() => L.Resolve(FinalBuildRowText);

    public static string ShopRow() => L.Resolve(ShopRowText);

    public static string BoardRow() => L.Resolve(BoardRowText);

    public static string StashRow() => L.Resolve(StashRowText);

    public static string Close() => L.Resolve(CloseText);
}
