#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LegendaryPosition;

internal static class LegendaryPositionSettingsDockEntry
{
    internal static CyclingSettingsDockEntry<LegendaryPositionDisplayMode> Create(
        Action? refreshUi = null
    ) =>
        new CyclingSettingsDockEntry<LegendaryPositionDisplayMode>(
            BppSettingsDockOrder.LegendaryPosition,
            "LegendaryPositionDisplay",
            LegendaryPositionSettingsMenuLabel.Resolve,
            new[]
            {
                LegendaryPositionDisplayMode.Default,
                LegendaryPositionDisplayMode.Blank,
                LegendaryPositionDisplayMode.Fixed999999,
                LegendaryPositionDisplayMode.PositionWithRating,
            },
            config =>
                config.LegendaryPositionDisplayModeConfig?.Value
                ?? LegendaryPositionDisplayMode.Default,
            (config, mode) =>
            {
                var entry = config.LegendaryPositionDisplayModeConfig;
                if (entry != null)
                    entry.Value = mode;
            },
            mode => mode != LegendaryPositionDisplayMode.Default,
            ResolveStatus,
            onChanged: _ =>
            {
                if (refreshUi != null)
                    refreshUi();
                else
                    LegendaryPositionUiRefresh.TryRefreshVisibleDisplays();
            }
        );

    private static string ResolveStatus(LegendaryPositionDisplayMode mode, string languageCode)
    {
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return mode switch
            {
                LegendaryPositionDisplayMode.Default => "默认",
                LegendaryPositionDisplayMode.Blank => "无人知晓",
                LegendaryPositionDisplayMode.Fixed999999 => "战力爆表",
                LegendaryPositionDisplayMode.PositionWithRating => "双显模式",
                _ => "默认",
            };
        }

        return mode switch
        {
            LegendaryPositionDisplayMode.Default => "DEF",
            LegendaryPositionDisplayMode.Blank => "BLANK",
            LegendaryPositionDisplayMode.Fixed999999 => "999999",
            LegendaryPositionDisplayMode.PositionWithRating => "P|R",
            _ => "DEF",
        };
    }
}
