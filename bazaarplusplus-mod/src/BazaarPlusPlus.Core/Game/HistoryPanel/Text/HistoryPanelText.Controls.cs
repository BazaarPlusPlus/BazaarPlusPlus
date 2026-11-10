#nullable enable

using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HistoryPanel;

// The labels of History's own controls, the same words on both hosts.
public static partial class HistoryPanelText
{
    private static readonly LocalizedTextSet NewerText = new("Newer", "较新", "較新");

    private static readonly LocalizedTextSet OlderText = new("Older", "较早", "較早");

    private static readonly LocalizedTextSet LatestText = new("Latest", "最新", "最新");

    private static readonly LocalizedTextSet AllHeroesText = new(
        "All heroes",
        "全部英雄",
        "全部英雄"
    );

    private static readonly LocalizedTextSet FiltersText = new("Filters", "筛选", "篩選");

    private static readonly LocalizedTextSet FilterHeroText = new("Hero", "英雄", "英雄");

    private static readonly LocalizedTextSet FilterOutcomeText = new("Result", "胜负", "勝負");

    private static readonly LocalizedTextSet FilterDayText = new("Day", "天数", "天數");

    private static readonly LocalizedTextSet OutcomeAllText = new("All", "全部", "全部");

    // Ghost battles are read from the local player's side, so the filter says whose win it is.
    private static readonly LocalizedTextSet OutcomeWonText = new("Won", "我赢了", "我贏了");

    private static readonly LocalizedTextSet OutcomeLostText = new("Lost", "我输了", "我輸了");

    private static readonly LocalizedTextSet CloudWindowText = new(
        "Cloud keeps the latest 5 days, up to 200 battles.",
        "云端保留最近 5 天，最多 200 场。",
        "雲端保留最近 5 天，最多 200 場。"
    );

    private static readonly LocalizedTextSet MoreText = new("More", "更多", "更多");

    private static readonly LocalizedTextSet CommunitySupportText = new(
        "Community support",
        "玩家社区支持",
        "玩家社群支持"
    );

    private static readonly LocalizedTextSet SkillsText = new("Skills", "技能", "技能");

    private static readonly LocalizedTextSet NoSkillsText = new("No skills", "无技能", "無技能");

    private static readonly LocalizedTextSet PickSideText = new(
        "Pick a side to see its skills.",
        "选择一方查看技能详情。",
        "選擇一方查看技能詳情。"
    );

    private static readonly LocalizedTextSet BackToRecordsText = new(
        "‹ Records",
        "‹ 记录",
        "‹ 記錄"
    );

    public static string Newer() => Resolve(NewerText);

    public static string Older() => Resolve(OlderText);

    public static string Latest() => Resolve(LatestText);

    public static string AllHeroes() => Resolve(AllHeroesText);

    public static string Filters() => Resolve(FiltersText);

    public static string FilterHero() => Resolve(FilterHeroText);

    public static string FilterOutcome() => Resolve(FilterOutcomeText);

    public static string FilterDay() => Resolve(FilterDayText);

    public static string OutcomeFilter(GhostBattleFilter filter) =>
        filter switch
        {
            GhostBattleFilter.IWon => Resolve(OutcomeWonText),
            GhostBattleFilter.ILost => Resolve(OutcomeLostText),
            _ => Resolve(OutcomeAllText),
        };

    public static string CloudWindow() => Resolve(CloudWindowText);

    public static string More() => Resolve(MoreText);

    public static string CommunitySupport() => Resolve(CommunitySupportText);

    public static string Skills() => Resolve(SkillsText);

    public static string NoSkills() => Resolve(NoSkillsText);

    public static string PickSide() => Resolve(PickSideText);

    public static string SkillsHidden(int hidden) =>
        FormatSimple(
            $"{hidden} more not shown. ",
            $"另有 {hidden} 个未显示。",
            $"另有 {hidden} 個未顯示。"
        );

    public static string BackToRecords() => Resolve(BackToRecordsText);
}
