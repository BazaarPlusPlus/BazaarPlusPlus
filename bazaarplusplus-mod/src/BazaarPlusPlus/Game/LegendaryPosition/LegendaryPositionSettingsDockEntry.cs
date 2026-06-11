#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.LegendaryPosition;

internal sealed class LegendaryPositionSettingsDockEntry : ISettingsDockEntry
{
    public int Order => BppSettingsDockOrder.LegendaryPosition;

    public BppSettingsDockDefinition Build(IBppConfig config) =>
        new(
            "LegendaryPositionDisplay",
            LegendaryPositionSettingsMenuLabel.Resolve,
            languageCode => ResolveStatus(ReadMode(config), languageCode),
            () => IsOverrideActive(config),
            () => CycleMode(config),
            collapseAfterActivate: false
        );

    private static LegendaryPositionDisplayMode ReadMode(IBppConfig config) =>
        config.LegendaryPositionDisplayModeConfig?.Value ?? LegendaryPositionDisplayMode.Default;

    private static bool IsOverrideActive(IBppConfig config) =>
        ReadMode(config) != LegendaryPositionDisplayMode.Default;

    private static void CycleMode(IBppConfig config)
    {
        var entry = config.LegendaryPositionDisplayModeConfig;
        if (entry == null)
            return;

        entry.Value = entry.Value switch
        {
            LegendaryPositionDisplayMode.Default => LegendaryPositionDisplayMode.Blank,
            LegendaryPositionDisplayMode.Blank => LegendaryPositionDisplayMode.Fixed999999,
            LegendaryPositionDisplayMode.Fixed999999 =>
                LegendaryPositionDisplayMode.PositionWithRating,
            _ => LegendaryPositionDisplayMode.Default,
        };

        LegendaryPositionUiRefresh.TryRefreshVisibleDisplays();
    }

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
