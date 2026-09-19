#nullable enable

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal sealed class TenWinBuildCorpus
{
    internal TenWinBuildCorpus(int buildCount)
    {
        BuildCount = buildCount;
    }

    internal int BuildCount { get; }
}

internal sealed class BuildRecommendationRepository
{
    private readonly Func<Task<BuildRecommendationRemoteRefreshResult>> _refresh;

    internal BuildRecommendationRepository(
        Func<Task<BuildRecommendationRemoteRefreshResult>> refresh
    )
    {
        _refresh = refresh;
    }

    internal Task<BuildRecommendationRemoteRefreshResult> TryRefreshFinalBuildsFromRemoteAsync() =>
        _refresh();
}
