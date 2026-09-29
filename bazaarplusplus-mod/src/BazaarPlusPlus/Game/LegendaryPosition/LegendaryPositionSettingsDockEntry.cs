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

    private static readonly LocalizedTextSet DefaultStatus = new("DEF", "默认");
    private static readonly LocalizedTextSet BlankStatus = new("BLANK", "无人知晓");
    private static readonly LocalizedTextSet FixedStatus = new("999999", "战力爆表");
    private static readonly LocalizedTextSet PositionWithRatingStatus = new("P|R", "双显模式");

    private static string ResolveStatus(LegendaryPositionDisplayMode mode, string languageCode)
    {
        var status = mode switch
        {
            LegendaryPositionDisplayMode.Blank => BlankStatus,
            LegendaryPositionDisplayMode.Fixed999999 => FixedStatus,
            LegendaryPositionDisplayMode.PositionWithRating => PositionWithRatingStatus,
            _ => DefaultStatus,
        };
        return status.Resolve(languageCode, L.CurrentMode);
    }
}
