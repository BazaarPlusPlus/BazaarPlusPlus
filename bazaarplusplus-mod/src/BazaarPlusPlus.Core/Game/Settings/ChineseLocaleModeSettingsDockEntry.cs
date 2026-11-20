#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

public static class ChineseLocaleModeSettingsDockEntry
{
    public static CyclingSettingsDockEntry<BppChineseLocaleMode> Create(Action onChanged)
    {
        if (onChanged == null)
            throw new ArgumentNullException(nameof(onChanged));

        return new CyclingSettingsDockEntry<BppChineseLocaleMode>(
            BppSettingsDockOrder.ChineseLocaleMode,
            "ChineseLocaleMode",
            ResolveChineseLocaleModeLabel,
            new[] { BppChineseLocaleMode.Mainland, BppChineseLocaleMode.Taiwan },
            config => ChineseScriptConverter.NormalizeMode(config.ChineseLocaleModeConfig.Value),
            (config, mode) =>
            {
                config.ChineseLocaleModeConfig.Value = mode;
            },
            mode => mode != BppChineseLocaleMode.Mainland,
            (mode, _) => ChineseScriptConverter.ResolveModeStatus(mode),
            onChanged: _ => onChanged()
        );
    }

    private static string ResolveChineseLocaleModeLabel(string languageCode)
    {
        return L.Resolve(new LocalizedTextSet("Chinese Locale", "中文模式"));
    }
}
