#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

public static class BazaarAgentCardSize
{
    /// <summary>Maps a card's wire-format size name to its socket width. The fallback is
    /// caller-specific and load-bearing: target-selection emission uses 1 (unknown sizes still
    /// get placement options), move/select enumeration uses 0 (unknown sizes yield no targets).</summary>
    public static int Parse(string? size, int fallback) =>
        size switch
        {
            "Small" => 1,
            "Medium" => 2,
            "Large" => 3,
            _ => fallback,
        };
}
