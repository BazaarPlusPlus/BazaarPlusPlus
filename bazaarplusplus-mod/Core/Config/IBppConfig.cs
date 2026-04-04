#nullable enable
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal interface IBppConfig
{
    ConfigEntry<bool>? UseNativeMonsterPreviewConfig { get; }

    ConfigEntry<bool>? EnableNameOverrideConfig { get; }

    ConfigEntry<bool>? EnchantPreviewAlwaysShowConfig { get; }

    ConfigEntry<bool>? EnableCombatStatusBarConfig { get; }

    ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; }

    ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; }

    ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; }

    ConfigEntry<bool>? EnableCommunityContributionConfig { get; }
}
