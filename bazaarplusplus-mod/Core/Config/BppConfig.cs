#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal sealed class BppConfig : IBppConfig
{
    public ConfigEntry<string>? ItemBoardAnchoredPositionConfig { get; private set; }

    public ConfigEntry<bool>? EnableNameOverrideConfig { get; private set; }

    public ConfigEntry<bool>? EnchantPreviewAlwaysShowConfig { get; private set; }

    public ConfigEntry<bool>? EnableCombatStatusBarConfig { get; private set; }

    public ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; private set; }

    public ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<BppChineseLocaleMode>? ChineseLocaleModeConfig { get; private set; }

    public ConfigEntry<LegendaryPositionDisplayMode>? LegendaryPositionDisplayModeConfig
    {
        get;
        private set;
    }

    public ConfigEntry<string>? ModApiV3BaseUrlConfig { get; private set; }

    public ConfigEntry<string>? FinalBuildsRemoteUrlConfig { get; private set; }

    public ConfigEntry<string>? SponsorListUrlConfig { get; private set; }

    public void Initialize(ConfigFile config)
    {
        ItemBoardAnchoredPositionConfig = config.Bind(
            "ItemBoard",
            "AnchoredPosition",
            "auto",
            "Anchored position override for the standalone item board overlay. Use 'auto' to follow the source tooltip, or 'x,y' such as '320,-40'."
        );
        EnableNameOverrideConfig = config.Bind(
            "StreamerMode",
            "EnableNameOverride",
            false,
            "Whether to set the in-game display name to Anonymous"
        );
        EnchantPreviewAlwaysShowConfig = config.Bind(
            "EnchantPreview",
            "AlwaysShow",
            true,
            "Whether to always show enchant preview text in item tooltips. If disabled, hold Ctrl to show it."
        );
        EnableCombatStatusBarConfig = config.Bind(
            "CombatStatusBar",
            "Enabled",
            false,
            "Whether to show the combat status bar with elapsed time, speed controls, and pause controls"
        );
        CombatStatusBarSpeedMultiplierConfig = config.Bind(
            "CombatStatusBar",
            "SpeedMultiplier",
            1.0f,
            "Default combat playback speed multiplier. The speed buttons cycle between 0.50, 0.67, and 1.00."
        );
        EnchantPreviewHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "EnchantPreview",
            "<Keyboard>/ctrl",
            "Binding path for enchant preview tooltip mode."
        );
        UpgradePreviewHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "UpgradePreview",
            "<Keyboard>/shift",
            "Binding path for upgrade preview tooltip mode."
        );
        ChineseLocaleModeConfig = config.Bind(
            "Localization",
            "ChineseLocaleMode",
            BppChineseLocaleMode.Mainland,
            "Chinese locale variant for BazaarPlusPlus UI when the game language is Chinese. Cycles between Mainland, Taiwan, and HongKong."
        );
        LegendaryPositionDisplayModeConfig = config.Bind(
            "LegendaryPositionDisplay",
            "Mode",
            LegendaryPositionDisplayMode.Default,
            "How BazaarPlusPlus should rewrite native Legendary leaderboard position labels. Default keeps the original value, Blank clears it, Fixed999999 forces 999999, and PositionWithRating shows '#position | rating'."
        );
        ModApiV3BaseUrlConfig = config.Bind(
            "Network",
            "ModApiV3BaseUrl",
            "https://api.example.com",
            "Base URL for V3 mod API endpoints. Override to route uploads/ghost services to another host."
        );
        FinalBuildsRemoteUrlConfig = config.Bind(
            "Network",
            "FinalBuildsRemoteUrl",
            "https://api.example.com/final_builds_for_mod.json",
            "Remote URL for downloading final build recommendations used by monster preview."
        );
        SponsorListUrlConfig = config.Bind(
            "Network",
            "SponsorListUrl",
            "https://api.example.com/supporter-list.json",
            "Remote URL for downloading supporter list used by monster sponsor banner."
        );
    }
}
