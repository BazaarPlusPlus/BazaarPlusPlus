using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.LiveBuild;

/// <summary>
/// The Ten-Win Builds recommendation repository and its shared supporter data. Only Warm starts
/// the build catalog; opening another panel that credits supporters does not load the corpus.
/// </summary>
internal sealed class AndroidLiveBuildData
{
    private readonly AndroidSupporterData _supporters;
    private bool _stopped;

    public AndroidLiveBuildData(
        BuildRecommendationRepository recommendations,
        AndroidSupporterData supporters
    )
    {
        ArgumentNullException.ThrowIfNull(recommendations);
        ArgumentNullException.ThrowIfNull(supporters);
        Recommendations = recommendations;
        _supporters = supporters;
    }

    public BuildRecommendationRepository Recommendations { get; }

    public bool Warmed { get; private set; }

    /// <summary>
    /// Main thread. The first call starts both catalogs' loads in the background: the corpus
    /// queues its remote fetch when the cache is missing or stale, as the desktop's panel does at
    /// mount. Later calls do nothing.
    /// </summary>
    public void Warm(string source)
    {
        if (Warmed || _stopped)
            return;
        Warmed = true;
        Recommendations.BeginCorpusLoad();
        _supporters.Warm();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.live_build.data_warmed"),
            ("source", source)
        );
    }

    /// <summary>
    /// The next supporters of the rotation, or none while the list has not loaded yet: the
    /// placeholder fallback is never credited.
    /// </summary>
    public IReadOnlyList<BPPSupporterSample> NextSupporters(int count) =>
        Warmed && !_stopped ? _supporters.Next(count) : Array.Empty<BPPSupporterSample>();

    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
    }
}
