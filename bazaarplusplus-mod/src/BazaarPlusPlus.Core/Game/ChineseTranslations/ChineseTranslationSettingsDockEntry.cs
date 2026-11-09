#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.ChineseTranslations;

public static class ChineseTranslationSettingsDockEntry
{
    private static readonly LocalizedTextSet Label = new(
        "Improve Simplified Chinese Translation",
        "优化简体中文翻译",
        "優化簡體中文翻譯"
    );

    public static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.ChineseTranslationImprovements,
            "ChineseTranslationImprovements",
            language => Label.Resolve(language, L.CurrentMode),
            config => config.EnableChineseTranslationImprovementsConfig.Value,
            (config, enabled) => config.EnableChineseTranslationImprovementsConfig.Value = enabled,
            isVisible: ChineseTranslationOverrides.AppliesTo
        );
}
