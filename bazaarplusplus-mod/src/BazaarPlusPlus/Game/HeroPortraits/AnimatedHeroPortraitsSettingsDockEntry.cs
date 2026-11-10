#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.HeroPortraits;

internal static class AnimatedHeroPortraitsSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new(
        "Animated Portraits",
        "动态英雄头像",
        "動態英雄頭像"
    );

    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.AnimatedHeroPortraits,
            "AnimatedHeroPortraits",
            languageCode => Labels.Resolve(languageCode, L.CurrentMode),
            config => config.EnableAnimatedHeroPortraitsConfig.Value,
            (config, enabled) => config.EnableAnimatedHeroPortraitsConfig.Value = enabled
        );
}
