#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

internal static class PanelButtonSettingsDockEntries
{
    private static readonly LocalizedTextSet CollectionLabels = new(
        "Collection Button",
        "图鉴按钮",
        "圖鑑按鈕"
    );
    private static readonly LocalizedTextSet TenWinBuildsLabels = new(
        "Ten-Win Builds Button",
        "十胜阵容按钮",
        "十勝陣容按鈕"
    );

    internal static void RegisterAll(SettingsDockEntryRegistry registry)
    {
        registry.Register(
            CyclingSettingsDockEntry<bool>.Toggle(
                BppSettingsDockOrder.ShowCollectionButton,
                "ShowCollectionButton",
                languageCode => CollectionLabels.Resolve(languageCode, L.CurrentMode),
                config => config.ShowCollectionButtonConfig.Value,
                (config, enabled) => config.ShowCollectionButtonConfig.Value = enabled
            )
        );
        registry.Register(
            CyclingSettingsDockEntry<bool>.Toggle(
                BppSettingsDockOrder.ShowTenWinBuildsButton,
                "ShowTenWinBuildsButton",
                languageCode => TenWinBuildsLabels.Resolve(languageCode, L.CurrentMode),
                config => config.ShowTenWinBuildsButtonConfig.Value,
                (config, enabled) => config.ShowTenWinBuildsButtonConfig.Value = enabled
            )
        );
    }
}
