#nullable enable

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal enum VoiceSubtitlesLogReasonCode
{
    Mount,
    MountException,
    EmptyText,
    SettingsApplyException,
    PlaybackStopped,
    FallbackTimeout,
    PlaybackQueryException,
    TraditionalGlyphsMissing,
}

internal enum VoiceSubtitlesSettingsPhase
{
    Mount,
    Show,
}
