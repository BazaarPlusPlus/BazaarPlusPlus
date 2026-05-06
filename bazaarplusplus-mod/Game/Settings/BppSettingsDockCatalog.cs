#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.LegendaryPosition;
using BazaarPlusPlus.Game.NameOverride;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;
using HistoryPanelFeature = BazaarPlusPlus.Game.HistoryPanel.HistoryPanel;
using HistoryPanelLabel = BazaarPlusPlus.Game.HistoryPanel.HistoryPanelSettingsMenuLabel;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockCatalog
{
    private static IBppConfig? _config;

    public static void Install(IBppConfig config) =>
        _config = config ?? throw new ArgumentNullException(nameof(config));

    private static IBppConfig Config =>
        _config
        ?? throw new InvalidOperationException(
            "BppSettingsDockCatalog.Install must be called at startup."
        );

    internal static IReadOnlyList<BppSettingsDockDefinition> Definitions { get; } =
    [
        new(
            "GameHistory",
            HistoryPanelLabel.Resolve,
            ResolveHistoryPanelStatus,
            IsHistoryPanelActionable,
            HistoryPanelFeature.OpenFromDockEntry,
            collapseAfterActivate: true
        ),
        new(
            "NameOverride",
            NameOverrideSettingsMenuLabel.Resolve,
            new NameOverrideSettingsMenuBridge(
                ReadNameOverrideEnabled,
                WriteNameOverrideEnabled,
                NameOverrideUiRefresh.TryRefreshVisibleHeroBanners
            )
        ),
        new(
            "LegendaryPositionDisplay",
            LegendaryPositionSettingsMenuLabel.Resolve,
            languageCode =>
                ResolveLegendaryPositionDisplayStatus(
                    ReadLegendaryPositionDisplayMode(),
                    languageCode
                ),
            IsLegendaryPositionDisplayOverrideActive,
            CycleLegendaryPositionDisplayMode,
            collapseAfterActivate: false
        ),
        new(
            "EnchantPreview",
            EnchantPreviewSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(ReadEnchantPreviewEnabled, WriteEnchantPreviewEnabled)
        ),
        new(
            "CombatStatusBar",
            CombatStatusBarSettingsMenuLabel.Resolve,
            new CombatStatusBarSettingsMenuBridge(
                CombatStatusBarFeature.GetEnabledSettingValue,
                CombatStatusBarFeature.SetEnabledSettingValue
            )
        ),
        new(
            "ChineseLocaleMode",
            ResolveChineseLocaleModeLabel,
            _ => BppChineseLocalization.ResolveModeStatus(ReadChineseLocaleMode()),
            IsChineseLocaleOverrideActive,
            CycleChineseLocaleMode,
            collapseAfterActivate: false
        ),
    ];

    private static bool ReadNameOverrideEnabled()
    {
        return Config.EnableNameOverrideConfig?.Value ?? false;
    }

    private static string ResolveHistoryPanelStatus(string languageCode)
    {
        if (TheBazaar.Data.IsInCombat)
            return HistoryPanelLabel.ResolveInRunStatus(languageCode);

        return HistoryPanelFeature.IsVisible
            ? HistoryPanelLabel.ResolveOpenStatus(languageCode)
            : HistoryPanelLabel.ResolveViewStatus(languageCode);
    }

    private static bool IsHistoryPanelActionable()
    {
        return !TheBazaar.Data.IsInCombat;
    }

    private static void WriteNameOverrideEnabled(bool enabled)
    {
        var config = Config.EnableNameOverrideConfig;
        if (config != null)
            config.Value = enabled;
    }

    private static bool ReadEnchantPreviewEnabled()
    {
        return Config.EnchantPreviewAlwaysShowConfig?.Value ?? false;
    }

    private static void WriteEnchantPreviewEnabled(bool enabled)
    {
        var config = Config.EnchantPreviewAlwaysShowConfig;
        if (config != null)
            config.Value = enabled;
    }

    private static string ResolveChineseLocaleModeLabel(string languageCode)
    {
        return new LocalizedTextSet("Chinese Locale", "中文模式").Resolve(languageCode);
    }

    private static BppChineseLocaleMode ReadChineseLocaleMode()
    {
        return Config.ChineseLocaleModeConfig?.Value ?? BppChineseLocaleMode.Mainland;
    }

    private static void CycleChineseLocaleMode()
    {
        var config = Config.ChineseLocaleModeConfig;
        if (config != null)
            config.Value = BppChineseLocalization.GetNextMode(config.Value);

        HistoryPanelFeature.RefreshLocalization();
    }

    private static bool IsChineseLocaleOverrideActive()
    {
        return ReadChineseLocaleMode() != BppChineseLocaleMode.Mainland;
    }

    private static LegendaryPositionDisplayMode ReadLegendaryPositionDisplayMode()
    {
        return Config.LegendaryPositionDisplayModeConfig?.Value
            ?? LegendaryPositionDisplayMode.Default;
    }

    private static void CycleLegendaryPositionDisplayMode()
    {
        var config = Config.LegendaryPositionDisplayModeConfig;
        if (config == null)
            return;

        config.Value = config.Value switch
        {
            LegendaryPositionDisplayMode.Default => LegendaryPositionDisplayMode.Blank,
            LegendaryPositionDisplayMode.Blank => LegendaryPositionDisplayMode.Fixed999999,
            LegendaryPositionDisplayMode.Fixed999999 =>
                LegendaryPositionDisplayMode.PositionWithRating,
            _ => LegendaryPositionDisplayMode.Default,
        };

        LegendaryPositionUiRefresh.TryRefreshVisibleDisplays();
    }

    private static bool IsLegendaryPositionDisplayOverrideActive()
    {
        return ReadLegendaryPositionDisplayMode() != LegendaryPositionDisplayMode.Default;
    }

    private static string ResolveLegendaryPositionDisplayStatus(
        LegendaryPositionDisplayMode mode,
        string languageCode
    )
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
