#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.Settings;

internal static class ChineseLocaleModeSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<BppChineseLocaleMode> Create(IBppEventBus eventBus)
    {
        if (eventBus == null)
            throw new ArgumentNullException(nameof(eventBus));

        return new CyclingSettingsDockEntry<BppChineseLocaleMode>(
            BppSettingsDockOrder.ChineseLocaleMode,
            "ChineseLocaleMode",
            ResolveChineseLocaleModeLabel,
            new[] { BppChineseLocaleMode.Mainland, BppChineseLocaleMode.Taiwan },
            config =>
                ChineseScriptConverter.NormalizeMode(
                    config.ChineseLocaleModeConfig?.Value ?? BppChineseLocaleMode.Mainland
                ),
            (config, mode) =>
            {
                var entry = config.ChineseLocaleModeConfig;
                if (entry != null)
                    entry.Value = mode;
            },
            mode => mode != BppChineseLocaleMode.Mainland,
            (mode, _) => ChineseScriptConverter.ResolveModeStatus(mode),
            onChanged: _ => eventBus.Publish(new ChineseLocaleModeChanged())
        );
    }

    private static string ResolveChineseLocaleModeLabel(string languageCode)
    {
        return L.Resolve(new LocalizedTextSet("Chinese Locale", "中文模式"));
    }
}
