using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Android.Ui;

/// <summary>The touch UI's own words; History's come from Core's History text.</summary>
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
    public static readonly LocalizedTextSet CollectionPanel = new(
        "Collection panel",
        "图鉴面板",
        "圖鑑面板"
    );
    public static readonly LocalizedTextSet On = new("On", "开", "開");
    public static readonly LocalizedTextSet Off = new("Off", "关", "關");
    public static readonly LocalizedTextSet GameUpdated = new(
        "The game has updated: download the new APK",
        "游戏已更新，请下载新版安装包",
        "遊戲已更新，請下載新版安裝包"
    );
}
