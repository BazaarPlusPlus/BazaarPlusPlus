#nullable enable
namespace BazaarPlusPlus.GameInterop.VoiceSubtitles;

internal sealed class VoiceSubtitleObserverCallbacks
{
    public static readonly VoiceSubtitleObserverCallbacks Empty = new(
        _ => VoiceSubtitleLookupResult.Empty,
        () => false,
        _ => { }
    );

    public VoiceSubtitleObserverCallbacks(
        Func<VoiceSubtitleLookupRequest, VoiceSubtitleLookupResult> resolveLine,
        Func<bool> isEnabled,
        Action<VoiceSubtitlePlaybackCue> queueShow
    )
    {
        ResolveLine = resolveLine ?? throw new ArgumentNullException(nameof(resolveLine));
        IsEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        QueueShow = queueShow ?? throw new ArgumentNullException(nameof(queueShow));
    }

    public Func<VoiceSubtitleLookupRequest, VoiceSubtitleLookupResult> ResolveLine { get; }

    public Func<bool> IsEnabled { get; }

    public Action<VoiceSubtitlePlaybackCue> QueueShow { get; }
}

internal readonly struct VoiceSubtitleLookupRequest
{
    public VoiceSubtitleLookupRequest(string lookupText, string sourceLabel, string hookName)
    {
        LookupText = lookupText ?? string.Empty;
        SourceLabel = sourceLabel ?? string.Empty;
        HookName = hookName ?? string.Empty;
    }

    public string LookupText { get; }

    public string SourceLabel { get; }

    public string HookName { get; }
}

internal readonly struct VoiceSubtitleLookupResult
{
    public static readonly VoiceSubtitleLookupResult Empty = new(
        VoiceSubtitleLine.Empty,
        hasLine: false,
        strategy: "none",
        matchedToken: null,
        catalogName: "none"
    );

    public VoiceSubtitleLookupResult(
        VoiceSubtitleLine line,
        bool hasLine,
        string strategy,
        string? matchedToken,
        string catalogName,
        int candidateCount = 0
    )
    {
        Line = line;
        HasLine = hasLine;
        Strategy = string.IsNullOrWhiteSpace(strategy) ? "unknown" : strategy;
        MatchedToken = matchedToken;
        CatalogName = string.IsNullOrWhiteSpace(catalogName) ? "unknown" : catalogName;
        CandidateCount = candidateCount;
    }

    public VoiceSubtitleLine Line { get; }

    public bool HasLine { get; }

    public string Strategy { get; }

    public string? MatchedToken { get; }

    public string CatalogName { get; }

    public int CandidateCount { get; }
}

internal readonly struct VoiceSubtitleLine
{
    public static readonly VoiceSubtitleLine Empty = new(
        string.Empty,
        string.Empty,
        string.Empty,
        0f
    );

    public VoiceSubtitleLine(string stem, string english, string chinese, float durationSeconds)
    {
        Stem = stem ?? string.Empty;
        English = english ?? string.Empty;
        Chinese = chinese ?? string.Empty;
        DurationSeconds = durationSeconds;
    }

    public string Stem { get; }

    public string English { get; }

    public string Chinese { get; }

    public float DurationSeconds { get; }
}

internal readonly struct VoiceSubtitlePlaybackCue
{
    public VoiceSubtitlePlaybackCue(
        VoiceSubtitleLine line,
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

    public VoiceSubtitleLine Line { get; }

    public float EventDurationSeconds { get; }

    public int AttemptId { get; }

    public Func<bool>? IsPlaybackStoppedOrStopping { get; }

    public Func<string>? PlaybackStateText { get; }
}
