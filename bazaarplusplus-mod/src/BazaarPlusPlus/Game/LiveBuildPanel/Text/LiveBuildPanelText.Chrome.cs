#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet TitleText = new(
        "Final Build",
        "终局阵容",
        "終局陣容",
        "終局陣容"
    );
    private static readonly LocalizedTextSet SubtitleText = new(
        "Choose live item candidates and browse matching ten-win builds.",
        "选择当前局内物品候选，并查看匹配的十胜阵容。",
        "選擇當前局內物品候選，並查看匹配的十勝陣容。",
        "選擇當前局內物品候選，並查看匹配的十勝陣容。"
    );
    private static readonly LocalizedTextSet FinalBuildRowText = new(
        "Ten-Win Build",
        "十胜推荐",
        "十勝推薦",
        "十勝推薦"
    );
    private static readonly LocalizedTextSet ShopRowText = new(
        "Shop Items",
        "商店物品",
        "商店物品",
        "商店物品"
    );
    private static readonly LocalizedTextSet BoardRowText = new(
        "Board Items",
        "场上物品",
        "場上物品",
        "場上物品"
    );
    private static readonly LocalizedTextSet StashRowText = new(
        "Stash Items",
        "箱子物品",
        "箱子物品",
        "箱子物品"
    );
    private static readonly LocalizedTextSet ResultCardTitleText = new(
        "Matches",
        "匹配结果",
        "匹配結果",
        "匹配結果"
    );
    private static readonly LocalizedTextSet CloseText = new("Close", "关闭", "關閉", "關閉");
    private static readonly LocalizedTextSet PrevText = new(
        "Previous",
        "上一条",
        "上一條",
        "上一條"
    );
    private static readonly LocalizedTextSet NextText = new("Next", "下一条", "下一條", "下一條");

    public static string Title() => L.Resolve(TitleText);

    public static string Subtitle() => L.Resolve(SubtitleText);

    public static string FinalBuildRow() => L.Resolve(FinalBuildRowText);

    public static string ShopRow() => L.Resolve(ShopRowText);

    public static string BoardRow() => L.Resolve(BoardRowText);

    public static string StashRow() => L.Resolve(StashRowText);

    public static string ResultCardTitle() => L.Resolve(ResultCardTitleText);

    public static string Close() => L.Resolve(CloseText);

    public static string Previous() => L.Resolve(PrevText);

    public static string Next() => L.Resolve(NextText);
}
