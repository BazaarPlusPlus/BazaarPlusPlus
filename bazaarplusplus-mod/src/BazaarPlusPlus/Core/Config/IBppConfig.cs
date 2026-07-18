#nullable enable
using BazaarPlusPlus.Localization;
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal interface IBppConfig
{
    ConfigEntry<bool>? EnableNameOverrideConfig { get; }

    ConfigEntry<PreviewVisibilityMode>? EnchantPreviewModeConfig { get; }

    ConfigEntry<bool>? EnableEventPreviewConfig { get; }

    ConfigEntry<bool>? EnableQuestPreviewConfig { get; }

    ConfigEntry<bool>? EnableCombatStatusBarConfig { get; }

    ConfigEntry<bool>? EnableBilingualItemNamesConfig { get; }

    ConfigEntry<bool>? EnableVoiceSubtitlesConfig { get; }

    ConfigEntry<SubtitlePosition>? VoiceSubtitlesPositionConfig { get; }

    ConfigEntry<SubtitleLanguageMode>? VoiceSubtitlesLanguageModeConfig { get; }

    ConfigEntry<float>? VoiceSubtitlesEnglishFontScaleConfig { get; }

    ConfigEntry<float>? VoiceSubtitlesChineseFontScaleConfig { get; }

    ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; }

    ConfigEntry<bool>? EndOfRunScreenshotEnabledConfig { get; }

    ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; }

    ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; }

    ConfigEntry<string>? ToggleCollectionPanelHotkeyPathConfig { get; }

    ConfigEntry<string>? ToggleLiveBuildPanelHotkeyPathConfig { get; }

    ConfigEntry<string>? ToggleHistoryPanelHotkeyPathConfig { get; }

    ConfigEntry<BppChineseLocaleMode>? ChineseLocaleModeConfig { get; }

    ConfigEntry<LegendaryPositionDisplayMode>? LegendaryPositionDisplayModeConfig { get; }

    ConfigEntry<bool>? BazaarDbUploadEnabled { get; }

    ConfigEntry<bool>? UseFixedSupporterListConfig { get; }
}
