#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.BilingualItemNames;

internal static class BilingualItemNamesSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new(
        "Bilingual Names",
        "双语名称",
        "雙語名稱",
        "Zweisprachige Namen",
        "Nomes Bilíngues",
        "이중 언어 이름",
        "Nomi Bilingue"
    );

    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.BilingualItemNames,
            "BilingualItemNames",
            languageCode => Labels.Resolve(languageCode, L.CurrentMode),
            config => config.EnableBilingualItemNamesConfig?.Value ?? false,
            (config, enabled) =>
            {
                var entry = config.EnableBilingualItemNamesConfig;
                if (entry != null)
                    entry.Value = enabled;
            }
        );
}
