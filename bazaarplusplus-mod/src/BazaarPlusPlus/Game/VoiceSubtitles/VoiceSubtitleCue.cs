#nullable enable
namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal readonly struct VoiceSubtitleCue
{
    public VoiceSubtitleCue(
        VoiceLine line,
        float eventDurationSeconds,
        int attemptId,
        Func<bool>? isPlaybackStoppedOrStopping,
        Func<string>? playbackStateText
    )
    {
        Line = line;
        EventDurationSeconds = eventDurationSeconds;
        AttemptId = attemptId;
        IsPlaybackStoppedOrStopping = isPlaybackStoppedOrStopping;
        PlaybackStateText = playbackStateText;
    }

    public VoiceLine Line { get; }

    public float EventDurationSeconds { get; }

    public int AttemptId { get; }

    public Func<bool>? IsPlaybackStoppedOrStopping { get; }

    public Func<string>? PlaybackStateText { get; }
}
