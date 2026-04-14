#nullable enable
using System.Collections.Generic;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Game.ItemEnchantPreview;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.NameOverride;
using BazaarPlusPlus.Game.RunLogging.Upload;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;
using HistoryPanelFeature = BazaarPlusPlus.Game.HistoryPanel.HistoryPanel;
using HistoryPanelLabel = BazaarPlusPlus.Game.HistoryPanel.HistoryPanelSettingsMenuLabel;

namespace BazaarPlusPlus.Game.Settings;

internal static class BppSettingsDockCatalog
{
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
            "EnchantPreview",
            ResolveEnchantPreviewLabel,
            ResolveEnchantPreviewStatus,
            IsEnchantPreviewModeHighlighted,
            CycleEnchantPreviewMode,
            collapseAfterActivate: false
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
            "NativeMonsterPreview",
            MonsterPreviewSettingsMenuLabel.Resolve,
            new SettingsMenuToggleBridge(
                ReadUseNativeMonsterPreview,
                WriteUseNativeMonsterPreview,
                MonsterPreviewModeSwitchCoordinator.Apply
            )
        ),
        new(
            "CommunityContribution",
            RunUploadSettingsMenuLabel.Resolve,
            _ => ReadCommunityContributionEnabled() ? "ON" : "OFF",
            ReadCommunityContributionEnabled,
            () => WriteCommunityContributionEnabled(!ReadCommunityContributionEnabled()),
            collapseAfterActivate: false,
            requiresCtrlToActivate: true
        ),
    ];

    private static bool ReadNameOverrideEnabled()
    {
        return BppRuntimeHost.Config.EnableNameOverrideConfig?.Value ?? false;
    }

    private static string ResolveHistoryPanelStatus(string languageCode)
    {
        if (!ReadCommunityContributionEnabled())
            return "OFF";

        if (BppRuntimeHost.RunContext.IsInGameRun)
            return HistoryPanelLabel.ResolveInRunStatus(languageCode);

        return HistoryPanelFeature.IsVisible
            ? HistoryPanelLabel.ResolveOpenStatus(languageCode)
            : HistoryPanelLabel.ResolveViewStatus(languageCode);
    }

    private static bool IsHistoryPanelActionable()
    {
        return ReadCommunityContributionEnabled() && !BppRuntimeHost.RunContext.IsInGameRun;
    }

    private static bool ReadCommunityContributionEnabled()
    {
        return BppRuntimeHost.Config.EnableCommunityContributionConfig?.Value ?? false;
    }

    private static void WriteCommunityContributionEnabled(bool enabled)
    {
        var config = BppRuntimeHost.Config.EnableCommunityContributionConfig;
        if (config != null)
            config.Value = enabled;
    }

    private static void WriteNameOverrideEnabled(bool enabled)
    {
        var config = BppRuntimeHost.Config.EnableNameOverrideConfig;
        if (config != null)
            config.Value = enabled;
    }

    private static bool ReadEnchantPreviewEnabled()
    {
        return BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig?.Value ?? true;
    }

    private static void WriteEnchantPreviewEnabled(bool enabled)
    {
        var config = BppRuntimeHost.Config.EnchantPreviewAlwaysShowConfig;
        if (config != null)
            config.Value = enabled;
    }

    private static string ResolveEnchantPreviewLabel(string languageCode)
    {
        return ReadEnchantPreviewEnabled()
            ? EnchantPreviewSettingsMenuLabel.ResolveAlwaysShow(languageCode)
            : EnchantPreviewSettingsMenuLabel.ResolveHoldToShow(languageCode);
    }

    private static string ResolveEnchantPreviewStatus(string _)
    {
        return ReadEnchantPreviewEnabled() ? "ON" : "OFF";
    }

    private static bool IsEnchantPreviewModeHighlighted()
    {
        return ReadEnchantPreviewEnabled();
    }

    private static void CycleEnchantPreviewMode()
    {
        WriteEnchantPreviewEnabled(!ReadEnchantPreviewEnabled());
    }

    private static bool ReadUseNativeMonsterPreview()
    {
        return BppRuntimeHost.Config.UseNativeMonsterPreviewConfig?.Value ?? false;
    }

    private static void WriteUseNativeMonsterPreview(bool enabled)
    {
        var config = BppRuntimeHost.Config.UseNativeMonsterPreviewConfig;
        if (config != null)
            config.Value = enabled;
    }
}
