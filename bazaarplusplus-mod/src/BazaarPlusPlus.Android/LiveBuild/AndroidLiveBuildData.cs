using BazaarPlusPlus.Android.Supporters;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Android.LiveBuild;

/// <summary>
/// What the Ten-Win Builds panel reads besides the live game, owned by the composition root: the
/// recommendation repository over the ten-win build catalog, and the supporter list
/// (<see cref="SupporterCatalogModule"/> over <see cref="AndroidSupporterCatalog"/>) that the
/// panel credits as the desktop's does. Nothing loads or fetches until <see cref="Warm"/>, which
/// the touch UI calls while the panel's setting is on, so a player who switched the panel off
/// never warms either catalog.
/// </summary>
internal sealed class AndroidLiveBuildData
{
    private readonly SupporterCatalogModule _supporters;
    private readonly BPPSupporterAttribution _attribution = new(Environment.TickCount);
    private bool _stopped;

    public AndroidLiveBuildData(
        BuildRecommendationRepository recommendations,
        BppSettings settings,
        string dataRoot
    )
    {
        ArgumentNullException.ThrowIfNull(recommendations);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        Recommendations = recommendations;
        BPPSupporterCatalog.Install(settings);
        _supporters = new SupporterCatalogModule(
            () => settings.UseFixedSupporterListConfig.Value,
            observer => AndroidSupporterCatalog.Create(dataRoot, observer),
            static () => DateTime.UtcNow,
            SupporterCatalogSettings.BuildCacheFilePath(dataRoot)
        );
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
        _supporters.Start();
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
        Warmed && BPPSupporterCatalog.HasEntries
            ? _attribution.Next(BPPSupporterCatalog.GetCurrentEntries(), count)
            : Array.Empty<BPPSupporterSample>();

    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        _supporters.Stop();
        BPPSupporterCatalog.Reset();
    }
}
