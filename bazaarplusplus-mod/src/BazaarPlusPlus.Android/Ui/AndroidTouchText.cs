using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>The touch UI's own words; everything else comes from Core's History text.</summary>
internal static class AndroidTouchText
{
    public static readonly LocalizedTextSet Settings = new("Settings", "设置", "設定");
    public static readonly LocalizedTextSet History = new("History", "历史记录", "歷史紀錄");
    public static readonly LocalizedTextSet UpgradePreview = new(
        "Upgrade preview",
        "升级预览",
        "升級預覽"
    );
    public static readonly LocalizedTextSet EnchantPreview = new(
        "Enchant preview",
        "附魔预览",
        "附魔預覽"
    );
    public static readonly LocalizedTextSet On = new("On", "开", "開");
    public static readonly LocalizedTextSet Off = new("Off", "关", "關");
    public static readonly LocalizedTextSet GameUpdated = new(
        "The game has updated: download the new APK",
        "游戏已更新，请下载新版安装包",
        "遊戲已更新，請下載新版安裝包"
    );
    public static readonly LocalizedTextSet NoRuns = new(
        "No runs recorded yet",
        "还没有记录的对局",
        "還沒有紀錄的對局"
    );
    public static readonly LocalizedTextSet Loading = new("Loading…", "加载中…", "載入中…");
    public static readonly LocalizedTextSet ReadFailed = new(
        "The history database could not be read",
        "无法读取历史数据库",
        "無法讀取歷史資料庫"
    );
    public static readonly LocalizedTextSet NoBattles = new(
        "No battles recorded in this run",
        "这一局没有记录的战斗",
        "這一局沒有紀錄的戰鬥"
    );
    public static readonly LocalizedTextSet Win = new("Win", "胜", "勝");
    public static readonly LocalizedTextSet Loss = new("Loss", "负", "負");
    public static readonly LocalizedTextSet More = new("more", "更多", "更多");
}
