#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal sealed class BppConfig : IBppConfig
{
    public ConfigEntry<bool>? UseNativeMonsterPreviewConfig { get; private set; }

    public ConfigEntry<bool>? EnableNameOverrideConfig { get; private set; }

    public ConfigEntry<bool>? EnchantPreviewAlwaysShowConfig { get; private set; }

    public ConfigEntry<bool>? EnableCombatStatusBarConfig { get; private set; }

    public ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; private set; }

    public ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<bool>? EnableCommunityContributionConfig { get; private set; }

    public void Initialize(ConfigFile config)
    {
        UseNativeMonsterPreviewConfig = config.Bind(
            "MonsterPreview",
            "UseNativePreview",
            true,
            "Whether monster preview should use the game's native preview instead of the BazaarPlusPlus overlay. Does not affect history panel battle previews."
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
        EnableCommunityContributionConfig = config.Bind(
            "CommunityContribution",
            "Enabled",
            false,
            "Whether to participate in BazaarPlusPlus community data contribution features, including background uploads and History Review access while out of a live run."
        );
    }
}
