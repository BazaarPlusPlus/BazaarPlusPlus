#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Data;

/// <summary>Provenance summary of a loaded corpus for status/feedback surfaces.</summary>
internal readonly struct TenWinCorpusSummary
{
    public TenWinCorpusSummary(
        DateTimeOffset? windowEndUtc,
        int buildCount,
        int heroCount,
        IReadOnlyList<TenWinHeroBuildCount>? heroBuildCounts = null
    )
    {
        WindowEndUtc = windowEndUtc;
        BuildCount = buildCount;
        HeroCount = heroCount;
        HeroBuildCounts = heroBuildCounts ?? Array.Empty<TenWinHeroBuildCount>();
    }

    public DateTimeOffset? WindowEndUtc { get; }

    public int BuildCount { get; }

    public int HeroCount { get; }

    public IReadOnlyList<TenWinHeroBuildCount> HeroBuildCounts { get; }
}

internal readonly struct TenWinHeroBuildCount
{
    public TenWinHeroBuildCount(string hero, int buildCount)
    {
        Hero = hero ?? string.Empty;
        BuildCount = buildCount;
    }

    public string Hero { get; }

    public int BuildCount { get; }
}
