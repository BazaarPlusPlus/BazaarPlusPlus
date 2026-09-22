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
