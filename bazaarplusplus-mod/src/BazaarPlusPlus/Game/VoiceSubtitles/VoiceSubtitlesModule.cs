#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.GameInterop.VoiceSubtitles;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal sealed class VoiceSubtitlesModule : IBppFeature
{
    private readonly IRemoteEmbeddedCatalog<VoiceLine[]> _catalog;

    internal VoiceSubtitlesModule()
        : this(VoiceLinesCatalogFactory.Create(BepInEx.Paths.GameRootPath)) { }

    internal VoiceSubtitlesModule(IRemoteEmbeddedCatalog<VoiceLine[]> catalog)
    {
        _catalog = catalog;
    }

    public void Start()
    {
        VoiceLineCatalog.Reset();
        VoiceLineDisplay.Reset();
        VoiceLineVoObserverBridge.Configure(
            new VoiceSubtitleObserverCallbacks(ResolveLine, VoiceSubtitlesGate.IsEnabled, QueueShow)
        );
        _ = _catalog.WarmAsync(CancellationToken.None).AsTask();
    }

    public void Stop()
    {
        VoiceLineVoObserverBridge.Configure(VoiceSubtitleObserverCallbacks.Empty);
        _catalog.Dispose();
        VoiceLineDisplay.Reset();
        VoiceLineCatalog.Reset();
    }

    private static VoiceSubtitleLookupResult ResolveLine(VoiceSubtitleLookupRequest request)
    {
        var resolution = VoiceLineCatalog.ResolveDetailed(
            request.LookupText,
            request.SourceLabel,
            request.HookName
        );
        var line = resolution.Line;
        return new VoiceSubtitleLookupResult(
            new VoiceSubtitleLine(line.Stem, line.English, line.Chinese, line.DurationSeconds),
            hasLine: !string.IsNullOrEmpty(line.Stem),
            resolution.Strategy,
            resolution.MatchedToken,
            resolution.CatalogName,
            resolution.CandidateCount
        );
    }

    private static void QueueShow(VoiceSubtitlePlaybackCue cue)
    {
        var line = cue.Line;
        VoiceLineDisplay.QueueShow(
            new VoiceSubtitleCue(
                new VoiceLine(line.Stem, line.English, line.Chinese, line.DurationSeconds),
                cue.EventDurationSeconds,
                cue.AttemptId,
                cue.IsPlaybackStoppedOrStopping,
                cue.PlaybackStateText
            )
        );
    }
}
