#nullable enable
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class VoiceSubtitlesSettingsMenuLabel
{
    private static readonly LocalizedTextSet Labels = new("Subtitle Mode", "字幕模式", "字幕模式");

    internal static string Resolve(string languageCode)
    {
        return Labels.Resolve(languageCode, L.CurrentMode);
    }
}
