using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>
/// The Android host's own words: the touch menu's and its rows in the game's settings dialog;
/// History's come from Core's History text.
/// </summary>
internal static class AndroidTouchText
{
    public static readonly LocalizedTextSet History = new("History", "历史记录", "歷史紀錄");
    public static readonly LocalizedTextSet UpgradePreview = new(
        "Upgrade preview",
        "升级预览",
        "升級預覽"
    );
    public static readonly LocalizedTextSet CollectionPanel = new(
        "Collection panel",
        "图鉴面板",
        "圖鑑面板"
    );
    public static readonly LocalizedTextSet HistoryPanel = new(
        "History panel",
        "历史记录面板",
        "歷史紀錄面板"
    );
    public static readonly LocalizedTextSet LiveBuildPanel = new(
        "Ten-Win Builds panel",
        "十胜阵容面板",
        "十勝陣容面板"
    );
    public static readonly LocalizedTextSet GameUpdated = new(
        "The game has updated: download the new APK",
        "游戏已更新，请下载新版安装包",
        "遊戲已更新，請下載新版安裝包"
    );
}
