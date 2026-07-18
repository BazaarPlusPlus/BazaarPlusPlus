#nullable enable
using BazaarPlusPlus.Localization;
using BepInEx.Configuration;

namespace BazaarPlusPlus.Core.Config;

internal sealed class BppConfig : IBppConfig
{
    internal const PreviewVisibilityMode DefaultEnchantPreviewMode = PreviewVisibilityMode.Always;
    internal const SubtitlePosition DefaultVoiceSubtitlesPosition = SubtitlePosition.TopCenter;

    public ConfigEntry<bool>? EnableNameOverrideConfig { get; private set; }

    public ConfigEntry<PreviewVisibilityMode>? EnchantPreviewModeConfig { get; private set; }

    public ConfigEntry<bool>? EnableEventPreviewConfig { get; private set; }

    public ConfigEntry<bool>? EnableQuestPreviewConfig { get; private set; }

    public ConfigEntry<bool>? EnableCombatStatusBarConfig { get; private set; }

    public ConfigEntry<bool>? EnableBilingualItemNamesConfig { get; private set; }

    public ConfigEntry<bool>? EnableVoiceSubtitlesConfig { get; private set; }

    public ConfigEntry<SubtitlePosition>? VoiceSubtitlesPositionConfig { get; private set; }

    public ConfigEntry<SubtitleLanguageMode>? VoiceSubtitlesLanguageModeConfig { get; private set; }

    public ConfigEntry<float>? VoiceSubtitlesEnglishFontScaleConfig { get; private set; }

    public ConfigEntry<float>? VoiceSubtitlesChineseFontScaleConfig { get; private set; }

    public ConfigEntry<float>? CombatStatusBarSpeedMultiplierConfig { get; private set; }

    public ConfigEntry<bool>? EndOfRunScreenshotEnabledConfig { get; private set; }

    public ConfigEntry<string>? EnchantPreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? UpgradePreviewHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? ToggleCollectionPanelHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? ToggleLiveBuildPanelHotkeyPathConfig { get; private set; }

    public ConfigEntry<string>? ToggleHistoryPanelHotkeyPathConfig { get; private set; }

    public ConfigEntry<BppChineseLocaleMode>? ChineseLocaleModeConfig { get; private set; }

    public ConfigEntry<LegendaryPositionDisplayMode>? LegendaryPositionDisplayModeConfig
    {
        get;
        private set;
    }

    public ConfigEntry<bool>? BazaarDbUploadEnabled { get; private set; }

    public ConfigEntry<bool>? UseFixedSupporterListConfig { get; private set; }

    public void Initialize(ConfigFile config)
    {
        EnableNameOverrideConfig = config.Bind(
            "StreamerMode",
            "EnableNameOverride",
            false,
            "Whether to set the in-game display name to Anonymous"
        );
        EnchantPreviewModeConfig = config.Bind(
            "EnchantPreview",
            "Mode",
            DefaultEnchantPreviewMode,
            "When to show enchant preview text in item tooltips. Off = hold Ctrl only. AutoOnPedestalChoice = auto-show while an enchant pedestal is offered on the choice screen, hold Ctrl otherwise. Always = append to every eligible tooltip."
        );
        EnableEventPreviewConfig = config.Bind(
            "EventPreview",
            "Enabled",
            true,
            "Whether to append the event-choice breakdown and hero level-up reward sections to native tooltips."
        );
        EnableQuestPreviewConfig = config.Bind(
            "QuestPreview",
            "Enabled",
            false,
            "Whether to show quest completion reward effects and aggregate-item missing-type hints in native tooltips."
        );
        EnableCombatStatusBarConfig = config.Bind(
            "CombatStatusBar",
            "Enabled",
            false,
            "Whether to show the combat status bar with elapsed time, speed controls, and pause controls"
        );
        EnableBilingualItemNamesConfig = config.Bind(
            "BilingualItemNames",
            "Enabled",
            false,
            "Whether item, skill, monster, pedestal, reward, and event tooltips show the other English/Chinese name below the current-language name."
        );
        EnableVoiceSubtitlesConfig = config.Bind(
            "VoiceSubtitles",
            "Enabled",
            false,
            "Whether Subtitle Mode enables voice-over subtitles. The in-game dock writes this together with VoiceSubtitles.Language."
        );
        VoiceSubtitlesPositionConfig = config.Bind(
            "VoiceSubtitles",
            "Position",
            DefaultVoiceSubtitlesPosition,
            "Where voice-over subtitles are anchored on screen."
        );
        VoiceSubtitlesLanguageModeConfig = config.Bind(
            "VoiceSubtitles",
            "Language",
            SubtitleLanguageMode.Both,
            "Language used by Subtitle Mode when voice-over subtitles are enabled: Both, ChineseOnly, or EnglishOnly."
        );
        VoiceSubtitlesEnglishFontScaleConfig = config.Bind(
            "VoiceSubtitles",
            "EnglishFontScale",
            1.0f,
            new ConfigDescription(
                "Font scale for the English subtitle line.",
                new AcceptableValueRange<float>(1.0f, 2.5f)
            )
        );
        VoiceSubtitlesChineseFontScaleConfig = config.Bind(
            "VoiceSubtitles",
            "ChineseFontScale",
            1.0f,
            new ConfigDescription(
                "Font scale for the Chinese subtitle line.",
                new AcceptableValueRange<float>(1.0f, 2.5f)
            )
        );
        CombatStatusBarSpeedMultiplierConfig = config.Bind(
            "CombatStatusBar",
            "SpeedMultiplier",
            1.0f,
            "Default combat playback speed multiplier. The speed buttons cycle between 0.50, 0.67, and 1.00."
        );
        EndOfRunScreenshotEnabledConfig = config.Bind(
            "Screenshots",
            "EndOfRunEnabled",
            true,
            "Whether to capture the automatic end-of-run screenshot before continuing from the run summary."
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
        ToggleCollectionPanelHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "ToggleCollectionPanel",
            "<Keyboard>/tab",
            "Binding path for toggling the card collection panel."
        );
        ToggleLiveBuildPanelHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "ToggleLiveBuildPanel",
            "<Keyboard>/capsLock",
            "Binding path for toggling the final build panel."
        );
        ToggleHistoryPanelHotkeyPathConfig = config.Bind(
            "Hotkeys",
            "ToggleHistoryPanel",
            "<Keyboard>/f8",
            "Binding path for toggling the game history panel."
        );
        ChineseLocaleModeConfig = config.Bind(
            "Localization",
            "ChineseLocaleMode",
            BppChineseLocaleMode.Mainland,
            "Chinese locale variant for BazaarPlusPlus UI when the game language is Chinese. Cycles between Mainland and Taiwan."
        );
        ChineseLocaleModeConfig.Value = ChineseScriptConverter.NormalizeMode(
            ChineseLocaleModeConfig.Value
        );
        LegendaryPositionDisplayModeConfig = config.Bind(
            "LegendaryPositionDisplay",
            "Mode",
            LegendaryPositionDisplayMode.Default,
            "How BazaarPlusPlus should rewrite native Legendary leaderboard position labels. Default keeps the original value, Blank clears it, Fixed999999 forces 999999, and PositionWithRating shows '#position | rating'."
        );
        // BazaarDB
        BazaarDbUploadEnabled = config.Bind(
            "BazaarDB",
            "UploadScreenshots",
            false,
            "When enabled, end-of-run screenshot snapshots are uploaded to our server for BazaarDB delivery. Includes screenshots from past runs. You can turn this off at any time; we will stop uploading and never delete what was already sent."
        );
        UseFixedSupporterListConfig = config.Bind(
            "Supporters",
            "UseFixedSupporterList",
            false,
            "Whether supporter attribution should use the bundled fixed supporter list instead of the remote supporter list."
        );
    }
}
