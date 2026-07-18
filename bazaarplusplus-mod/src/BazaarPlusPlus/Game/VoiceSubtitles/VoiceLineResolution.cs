#nullable enable

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal readonly struct VoiceLineResolution
{
    public VoiceLineResolution(
        VoiceLine line,
        string strategy,
        string? matchedToken,
        string catalogName,
        int candidateCount = 0
    )
    {
        Line = line;
        Strategy = strategy;
        MatchedToken = matchedToken;
        CatalogName = catalogName;
        CandidateCount = candidateCount;
    }

    public VoiceLine Line { get; }

    public string Strategy { get; }

    public string? MatchedToken { get; }

    public string CatalogName { get; }

    public int CandidateCount { get; }
}
