#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LiveBuildPanel;

internal static partial class LiveBuildPanelText
{
    private static readonly LocalizedTextSet NoRunText = new(
        "No active run.",
        "当前没有进行中的对局。",
        "當前沒有進行中的對局。",
        "當前沒有進行中的對局。"
    );
    private static readonly LocalizedTextSet NoCandidatesText = new(
        "Select item candidates from shop, board, or stash.",
        "从商店、场上或箱子选择物品候选。",
        "從商店、場上或箱子選擇物品候選。",
        "從商店、場上或箱子選擇物品候選。"
    );
    private static readonly LocalizedTextSet NoRecommendationText = new(
        "No matching ten-win build.",
        "暂无匹配的十胜阵容。",
        "暫無匹配的十勝陣容。",
        "暫無匹配的十勝陣容。"
    );
    private static readonly LocalizedTextSet EmptyShopText = new(
        "No item options in the current shop selection.",
        "当前商店选择里没有物品。",
        "當前商店選擇裡沒有物品。",
        "當前商店選擇裡沒有物品。"
    );
    private static readonly LocalizedTextSet EmptyBoardText = new(
        "No board items.",
        "场上没有物品。",
        "場上沒有物品。",
        "場上沒有物品。"
    );
    private static readonly LocalizedTextSet EmptyStashText = new(
        "No stash items.",
        "箱子没有物品。",
        "箱子沒有物品。",
        "箱子沒有物品。"
    );

    private static readonly LocalizedTextSet UnknownText = new("unknown", "未知");

    public static string NoRun() => L.Resolve(NoRunText);

    public static string NoCandidates() => L.Resolve(NoCandidatesText);

    public static string NoRecommendation() => L.Resolve(NoRecommendationText);

    public static string EmptyShop() => L.Resolve(EmptyShopText);

    public static string EmptyBoard() => L.Resolve(EmptyBoardText);

    public static string EmptyStash() => L.Resolve(EmptyStashText);

    public static string Unknown() => L.Resolve(UnknownText);
}
