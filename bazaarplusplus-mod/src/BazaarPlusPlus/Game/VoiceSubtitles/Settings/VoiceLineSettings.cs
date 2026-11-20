#nullable enable

using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Patches;

namespace BazaarPlusPlus.Game.VoiceSubtitles.Settings;

internal sealed class VoiceLineSettings
{
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
                config.VoiceSubtitlesPositionConfig.Value,
                config.VoiceSubtitlesLanguageModeConfig.Value,
                config.VoiceSubtitlesEnglishFontScaleConfig.Value,
                config.VoiceSubtitlesChineseFontScaleConfig.Value
            );
        }
    }
}
