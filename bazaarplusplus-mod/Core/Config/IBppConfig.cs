#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal interface IBppConfig
{
    ConfigEntry<string>? ItemBoardAnchoredPositionConfig { get; }

    ConfigEntry<bool>? EnableNameOverrideConfig { get; }

    ConfigEntry<bool>? EnchantPreviewAlwaysShowConfig { get; }

    ConfigEntry<bool>? EnableCombatStatusBarConfig { get; }

    ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; }

    ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; }

    ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; }

    ConfigEntry<BppChineseLocaleMode>? ChineseLocaleModeConfig { get; }

    ConfigEntry<LegendaryPositionDisplayMode>? LegendaryPositionDisplayModeConfig { get; }

    ConfigEntry<string>? ModApiV3BaseUrlConfig { get; }

    ConfigEntry<string>? FinalBuildsRemoteUrlConfig { get; }

    ConfigEntry<string>? SponsorListUrlConfig { get; }
}
