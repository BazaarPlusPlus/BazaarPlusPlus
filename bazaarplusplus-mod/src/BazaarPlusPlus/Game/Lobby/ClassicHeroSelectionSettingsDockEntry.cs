#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Lobby;

internal static class ClassicHeroSelectionSettingsDockEntry
{
    private static readonly LocalizedTextSet Label = new(
        "Classic Hero Selection Layout",
        "经典英雄选择布局",
        "經典英雄選擇佈局"
    );

    internal static CyclingSettingsDockEntry<bool> Create() =>
        CyclingSettingsDockEntry<bool>.Toggle(
            BppSettingsDockOrder.ClassicHeroSelection,
            "ClassicHeroSelection",
            languageCode => Label.Resolve(languageCode, L.CurrentMode),
            config => config.EnableClassicHeroSelectionConfig.Value,
            (config, enabled) => config.EnableClassicHeroSelectionConfig.Value = enabled
        );
}
