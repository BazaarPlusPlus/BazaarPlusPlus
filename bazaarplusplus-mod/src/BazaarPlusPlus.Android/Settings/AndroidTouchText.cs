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
        "The Bazaar has an update: get it from its store",
        "The Bazaar 有更新，请从它的商店更新",
        "The Bazaar 有更新，請從它的商店更新"
    );
}
