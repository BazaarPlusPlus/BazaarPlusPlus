#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Core.Config;

internal sealed class BppSettings
{
    internal const PreviewVisibilityMode DefaultEnchantPreviewMode = PreviewVisibilityMode.Always;
    internal const HotkeyActivationMode DefaultUpgradePreviewActivationMode =
        HotkeyActivationMode.Hold;
    internal const SubtitlePosition DefaultVoiceSubtitlesPosition = SubtitlePosition.TopCenter;
    internal const float DefaultFsrSharpness = 0.92f;

    public ISetting<bool> EnableNameOverrideConfig { get; }

    public ISetting<PreviewVisibilityMode> EnchantPreviewModeConfig { get; }

    public ISetting<bool> EnableEventPreviewConfig { get; }

    public ISetting<bool> EnableQuestPreviewConfig { get; }

    public ISetting<bool> EnableBilingualItemNamesConfig { get; }

    public ISetting<bool> EnableCosmeticNamesConfig { get; }

    public ISetting<bool> EnableVoiceSubtitlesConfig { get; }

    public ISetting<SubtitlePosition> VoiceSubtitlesPositionConfig { get; }

    public ISetting<SubtitleLanguageMode> VoiceSubtitlesLanguageModeConfig { get; }

    public ISetting<float> VoiceSubtitlesEnglishFontScaleConfig { get; }

    public ISetting<float> VoiceSubtitlesChineseFontScaleConfig { get; }

    public ISetting<bool> EnableCombatStatusBarConfig { get; }

    public ISetting<float> CombatStatusBarSpeedMultiplierConfig { get; }

    public ISetting<bool> EndOfRunScreenshotEnabledConfig { get; }

    public ISetting<string> EnchantPreviewHotkeyPathConfig { get; }

    public ISetting<string> UpgradePreviewHotkeyPathConfig { get; }

    public ISetting<HotkeyActivationMode> UpgradePreviewActivationModeConfig { get; }

    public ISetting<string> ToggleCollectionPanelHotkeyPathConfig { get; }

    public ISetting<string> ToggleLiveBuildPanelHotkeyPathConfig { get; }

    public ISetting<string> ToggleHistoryPanelHotkeyPathConfig { get; }

    public ISetting<BppChineseLocaleMode> ChineseLocaleModeConfig { get; }

    public ISetting<LegendaryPositionDisplayMode> LegendaryPositionDisplayModeConfig { get; }

    public ISetting<GraphicsUpscalingMode> GraphicsUpscalingModeConfig { get; }

    public ISetting<float> GraphicsUpscalingSharpnessConfig { get; }

    public ISetting<bool> BazaarDbUploadEnabled { get; }

    public ISetting<bool> UseFixedSupporterListConfig { get; }

    public BppSettings(IBppSettingsStore config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

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
        EnableBilingualItemNamesConfig = config.Bind(
            "BilingualItemNames",
            "Enabled",
            false,
            "Whether item, skill, monster, pedestal, reward, and event tooltips show the other English/Chinese name below the current-language name."
        );
        EnableCosmeticNamesConfig = config.Bind(
            "CosmeticNames",
            "Enabled",
            false,
            "Whether to overlay localized names at the bottom center of individual cosmetic thumbnails in the loadout selection list."
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
            "Font scale for the English subtitle line.",
            new SettingRange<float>(1.0f, 2.5f)
        );
        VoiceSubtitlesChineseFontScaleConfig = config.Bind(
            "VoiceSubtitles",
            "ChineseFontScale",
            1.0f,
            "Font scale for the Chinese subtitle line.",
            new SettingRange<float>(1.0f, 2.5f)
        );
        EnableCombatStatusBarConfig = config.Bind(
            "CombatStatusBar",
            "Enabled",
            true,
            "Whether to show the combat status bar with elapsed time, speed controls, and pause controls."
        );
        CombatStatusBarSpeedMultiplierConfig = config.Bind(
            "CombatStatusBar",
            "SpeedMultiplier",
            1.0f,
            "Default combat playback speed multiplier. The speed button cycles between 0.50, 0.67, and 1.00."
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
        UpgradePreviewActivationModeConfig = config.Bind(
            "Hotkeys",
            "UpgradePreviewActivationMode",
            DefaultUpgradePreviewActivationMode,
            "How Shift behaves across BazaarPlusPlus preview features. Hold = active only while Shift is held. Toggle = each Shift press switches the upgrade preview on or off."
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
        GraphicsUpscalingModeConfig = config.Bind(
            "Graphics",
            "UpscalingMode",
            GraphicsUpscalingMode.Native,
            "Desktop FSR 1 render-resolution upscaling mode for macOS and Windows. Native preserves the game's original URP settings. Ultra Quality renders at 77%, Quality at 67%, Balanced at 59%, and Performance at 50% per axis."
        );
        GraphicsUpscalingSharpnessConfig = config.Bind(
            "Graphics",
            "FsrSharpness",
            DefaultFsrSharpness,
            "FSR 1 RCAS sharpening strength. 0 is softest and 1 is sharpest.",
            new SettingRange<float>(0f, 1f)
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
