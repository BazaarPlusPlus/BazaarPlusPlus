using BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;
using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Mobile.LiveBuild;

// Only Warm starts the build catalog. Crediting supporters in another panel leaves it cold.
internal sealed class TouchLiveBuildData
{
    private readonly ISupporterRotation _supporters;
    private bool _stopped;

    public TouchLiveBuildData(
        BuildRecommendationRepository recommendations,
        ISupporterRotation supporters
    )
    {
        Recommendations =
            recommendations ?? throw new ArgumentNullException(nameof(recommendations));
        _supporters = supporters ?? throw new ArgumentNullException(nameof(supporters));
    }

    public BuildRecommendationRepository Recommendations { get; }

    public bool Warmed { get; private set; }

    public void Warm(string source)
    {
        if (Warmed || _stopped)
            return;
        Warmed = true;
        Recommendations.BeginCorpusLoad();
        _supporters.Warm();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Touch, "touch.live_build.data_warmed"),
            ("source", source)
        );
    }

    // The fallback entry is a placeholder and never gets credited.
    public IReadOnlyList<BPPSupporterSample> NextSupporters(int count) =>
        Warmed && !_stopped ? _supporters.Next(count) : Array.Empty<BPPSupporterSample>();

    public void Stop() => _stopped = true;
}
