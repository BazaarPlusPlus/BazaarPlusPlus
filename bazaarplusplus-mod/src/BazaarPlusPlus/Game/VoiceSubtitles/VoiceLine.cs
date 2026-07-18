#nullable enable

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal readonly struct VoiceLine
{
    public VoiceLine(string stem, string english, string chinese, float durationSeconds)
    {
        Stem = stem;
        English = english;
        Chinese = chinese;
        DurationSeconds = durationSeconds;
    }

    public string Stem { get; }

    public string English { get; }

    public string Chinese { get; }

    public float DurationSeconds { get; }
}
