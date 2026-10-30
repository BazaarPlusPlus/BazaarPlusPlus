#nullable enable
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Touch;

/// <summary>
/// The Ten-Win Builds data a touch panel reads, as the Android host's <c>TouchLiveBuildData</c>:
/// the desktop's recommendation repository and the shared supporter rotation.
/// </summary>
internal sealed class TouchLiveBuildData
{
    private bool _stopped;

    public TouchLiveBuildData(BuildRecommendationRepository recommendations)
    {
        Recommendations =
            recommendations ?? throw new ArgumentNullException(nameof(recommendations));
    }

    public BuildRecommendationRepository Recommendations { get; }

    public bool Warmed { get; private set; }

    /// <summary>Main thread. The first call starts the corpus load; later calls do nothing.</summary>
    public void Warm(string source)
    {
        if (Warmed || _stopped)
            return;
        Warmed = true;
        Recommendations.BeginCorpusLoad();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.data_warmed"),
            ("source", source)
        );
    }

    /// <summary>The next supporters of the rotation.</summary>
    public IReadOnlyList<BPPSupporterSample> NextSupporters(int count) =>
        _stopped ? Array.Empty<BPPSupporterSample>() : BPPSupporters.SampleMany(count);

    public void Stop() => _stopped = true;
}
