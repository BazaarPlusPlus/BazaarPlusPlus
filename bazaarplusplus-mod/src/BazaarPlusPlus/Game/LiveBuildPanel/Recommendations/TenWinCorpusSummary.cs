#nullable enable

using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>Provenance summary of a loaded corpus for status/feedback surfaces.</summary>
internal readonly struct TenWinCorpusSummary
{
    public TenWinCorpusSummary(
        DateTimeOffset? generatedAtUtc,
        int buildCount,
        int heroCount,
        IReadOnlyList<TenWinHeroBuildCount>? heroBuildCounts = null
    )
    {
        GeneratedAtUtc = generatedAtUtc;
        BuildCount = buildCount;
        HeroCount = heroCount;
        HeroBuildCounts = heroBuildCounts ?? Array.Empty<TenWinHeroBuildCount>();
    }

    public DateTimeOffset? GeneratedAtUtc { get; }

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
