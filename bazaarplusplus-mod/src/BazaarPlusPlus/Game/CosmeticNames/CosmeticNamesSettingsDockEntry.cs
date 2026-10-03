#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.CosmeticNames;

internal static class CosmeticNamesSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new(
        "Cosmetic Names",
        "装饰品名称",
        "裝飾品名稱",
        "Kosmetiknamen",
        "Nomes dos cosméticos",
        "꾸미기 아이템 이름",
        "Nomi dei cosmetici"
    );

    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.CosmeticNames,
            "CosmeticNames",
            languageCode => Labels.Resolve(languageCode, L.CurrentMode),
            config => config.EnableCosmeticNamesConfig.Value,
            (config, enabled) =>
            {
                config.EnableCosmeticNamesConfig.Value = enabled;
            }
        );
}
