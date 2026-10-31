using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Android.Settings;

/// <summary>The Android host's own words for its rows in the game's settings dialog.</summary>
internal static class AndroidTouchText
{
    public static readonly LocalizedTextSet UpgradePreview = new(
        "Upgrade preview",
        "升级预览",
        "升級預覽"
    );
    public static readonly LocalizedTextSet GameUpdated = new(
        "The game has updated: download the new APK",
        "游戏已更新，请下载新版安装包",
        "遊戲已更新，請下載新版安裝包"
    );
}
