#nullable enable

using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Patches;

namespace BazaarPlusPlus.Game.VoiceSubtitles.Settings;

// Read-only snapshot of the voice-subtitle settings. Persistence lives in the shared
// BazaarPlusPlus.cfg (BppConfig, section "VoiceSubtitles"); there is no separate config file.
// Runtime callers with no config injection (VoiceLineDisplay) read through BppPatchHost, the
// same seam VoiceSubtitlesGate uses. UI writes go straight to the ConfigEntry from the dock
// entries, which are handed the IBppConfig instance.
internal sealed class VoiceLineSettings
{
    private const float DefaultEnglishFontScale = 1f;
    private const float DefaultChineseFontScale = 1f;

    private VoiceLineSettings(
        SubtitlePosition position,
        SubtitleLanguageMode languageMode,
        float englishFontScale,
        float chineseFontScale
    )
    {
        Position = position;
        LanguageMode = languageMode;
        EnglishFontScale = englishFontScale;
        ChineseFontScale = chineseFontScale;
    }

    public SubtitlePosition Position { get; }
    public SubtitleLanguageMode LanguageMode { get; }
    public float EnglishFontScale { get; }
    public float ChineseFontScale { get; }

    public static VoiceLineSettings Current
    {
        get
        {
            var config = BppPatchHost.Services.Config;
            return new VoiceLineSettings(
                config.VoiceSubtitlesPositionConfig?.Value
                    ?? BppConfig.DefaultVoiceSubtitlesPosition,
                config.VoiceSubtitlesLanguageModeConfig?.Value ?? SubtitleLanguageMode.Both,
                config.VoiceSubtitlesEnglishFontScaleConfig?.Value ?? DefaultEnglishFontScale,
                config.VoiceSubtitlesChineseFontScaleConfig?.Value ?? DefaultChineseFontScale
            );
        }
    }
}
