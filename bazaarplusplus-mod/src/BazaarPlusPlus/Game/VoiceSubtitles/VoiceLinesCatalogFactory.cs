#nullable enable
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class VoiceLinesCatalogFactory
{
    internal const string EmbeddedResourceName =
        "BazaarPlusPlus.Data.VoiceSubtitles.voice-lines.json";
    private const string RemoteUrl =
        "https://bazaarline-installer.bazaarplusplus.com/data/voice-lines.json";
    private const string CacheFileName = "voice-lines.json";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(20);
    private static readonly HttpClient HttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        userAgentSuffix: "VoiceSubtitlesRepository",
        timeout: TimeSpan.FromSeconds(10)
    );

    internal static IRemoteEmbeddedCatalog<VoiceLine[]> Create(string dataRootPath)
    {
        var cache = new FileCatalogCache(BuildCacheFilePath(dataRootPath));
        return new RemoteEmbeddedCatalog<VoiceLine[]>(
            new VoiceLinesCatalogParser(),
            new AssemblyResourceCatalogSource(
                Assembly.GetExecutingAssembly(),
                EmbeddedResourceName
            ),
            cache,
            new HttpRemoteCatalogSource(HttpClient, RemoteUrl),
            SystemCatalogClock.Instance,
            ThreadPoolCatalogRefreshScheduler.Instance,
            new VoiceLinesCatalogObserver(),
            CacheDuration
        );
    }

    internal static string BuildCacheFilePath(string dataRootPath) =>
        Path.Combine(dataRootPath, CacheFileName);
}

internal sealed class VoiceLinesCatalogParser : ICatalogParser<VoiceLine[]>
{
    public CatalogParseResult<VoiceLine[]> Parse(string document, CatalogSource source)
    {
        var lines = VoiceLinesDocument.Parse(document, MapSource(source));
        return CatalogParseResult<VoiceLine[]>.Success(lines);
    }

    private static VoiceCatalogSource MapSource(CatalogSource source) =>
        source switch
        {
            CatalogSource.Cache => VoiceCatalogSource.Cache,
            CatalogSource.Embedded => VoiceCatalogSource.Embedded,
            CatalogSource.Remote => VoiceCatalogSource.Remote,
            _ => VoiceCatalogSource.None,
        };
}

internal sealed class VoiceLinesCatalogObserver : IRemoteEmbeddedCatalogObserver<VoiceLine[]>
{
    private readonly object _sync = new();
    private VoiceCatalogState _state = VoiceCatalogState.Loading;
    private VoiceCatalogDegradation? _activeDegradation;

    public void OnWarmStarted() =>
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.VoiceSubtitles, "voice_subtitles.catalog.started"),
            static () => []
        );

    public void OnInitialLoad(CatalogInitialLoadResult<VoiceLine[]> result)
    {
        if (result.Snapshot is { } snapshot)
        {
            VoiceLineCatalog.ReplaceCatalog(snapshot.Value, CatalogName(snapshot.Source));
            if (IsInitialDegradation(snapshot.Issue))
            {
                var issue = snapshot.Issue!.Value;
                ReportDegraded(
                    MapReason(issue.Kind),
                    EventSource(issue.Kind, snapshot.Source),
                    issue.Exception
                );
            }
            else
            {
                lock (_sync)
                {
                    _state = VoiceCatalogState.Ready;
                    _activeDegradation = null;
                }
                BppLog.InfoEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.VoiceSubtitles,
                        "voice_subtitles.catalog.ready"
                    ),
                    ("source", MapSource(snapshot.Source)),
                    ("line_count", snapshot.Value.Length)
                );
            }
            return;
        }

        VoiceLineCatalog.Reset();
        var unavailable = result.Issue ?? new CatalogIssue(CatalogIssueKind.Unexpected);
        lock (_sync)
        {
            _state = VoiceCatalogState.Failed;
            _activeDegradation = null;
        }
        EmitFailed(
            MapReason(unavailable.Kind),
            EventSource(unavailable.Kind, CatalogSource.Embedded),
            unavailable.Exception
        );
    }

    public void OnRefreshQueued(CatalogIssue reason) =>
        BppLog.DebugEvent(
            new BppLogEvent(
                BppLogFeatureScope.VoiceSubtitles,
                "voice_subtitles.catalog_refresh.started"
            ),
            () =>
                [
                    ("reason_code", MapReason(reason.Kind)),
                    ("endpoint", VoiceCatalogEndpoint.VoiceCatalog),
                ]
        );

    public void OnRefreshCompleted(
        CatalogRefreshTrigger trigger,
        CatalogRefreshResult<VoiceLine[]> result
    )
    {
        if (result.Succeeded && result.Snapshot is { } snapshot)
        {
            VoiceLineCatalog.ReplaceCatalog(snapshot.Value, CatalogName(snapshot.Source));
            if (snapshot.Issue is { Kind: CatalogIssueKind.CacheWriteFailed } cacheIssue)
            {
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.VoiceSubtitles,
                        "voice_subtitles.catalog_cache.degraded",
                        storm: ["reason_code"]
                    ),
                    cacheIssue.Exception!,
                    ("reason_code", VoiceCatalogReasonCode.WriteFailed)
                );
            }

            VoiceCatalogDegradation? recovered;
            lock (_sync)
            {
                recovered = _state == VoiceCatalogState.Degraded ? _activeDegradation : null;
                _state = VoiceCatalogState.Ready;
                _activeDegradation = null;
            }
            if (recovered.HasValue)
            {
                BppLog.RecoverStorm(
                    new BppLogEvent(
                        BppLogFeatureScope.VoiceSubtitles,
                        "voice_subtitles.catalog.degraded",
                        storm: ["reason_code", "source"]
                    ),
                    ("reason_code", recovered.Value.ReasonCode),
                    ("source", recovered.Value.Source)
                );
                BppLog.InfoEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.VoiceSubtitles,
                        "voice_subtitles.catalog.recovered"
                    ),
                    ("reason_code", recovered.Value.ReasonCode),
                    ("source", recovered.Value.Source),
                    ("line_count", snapshot.Value.Length)
                );
            }
            return;
        }

        if (trigger != CatalogRefreshTrigger.Background)
            return;

        var issue = result.Issue ?? new CatalogIssue(CatalogIssueKind.Unexpected);
        ReportDegraded(MapReason(issue.Kind), VoiceCatalogSource.Remote, issue.Exception);
    }

    private void ReportDegraded(
        VoiceCatalogReasonCode reason,
        VoiceCatalogSource source,
        Exception? exception
    )
    {
        lock (_sync)
        {
            if (_state is VoiceCatalogState.Degraded or VoiceCatalogState.Failed)
                return;
            _state = VoiceCatalogState.Degraded;
            _activeDegradation = new VoiceCatalogDegradation(reason, source);
        }

        var fields = new BppLogField[]
        {
            ("reason_code", reason),
            ("source", source),
            ("endpoint", VoiceCatalogEndpoint.VoiceCatalog),
        };
        if (exception == null)
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.catalog.degraded",
                    storm: ["reason_code", "source"]
                ),
                fields
            );
        else
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.catalog.degraded",
                    storm: ["reason_code", "source"]
                ),
                exception,
                fields
            );
    }

    private static void EmitFailed(
        VoiceCatalogReasonCode reason,
        VoiceCatalogSource source,
        Exception? exception
    )
    {
        var fields = new BppLogField[] { ("reason_code", reason), ("source", source) };
        if (exception == null)
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.catalog.failed"
                ),
                fields
            );
        else
            BppLog.ErrorEvent(
                new BppLogEvent(
                    BppLogFeatureScope.VoiceSubtitles,
                    "voice_subtitles.catalog.failed"
                ),
                exception,
                fields
            );
    }

    private static bool IsInitialDegradation(CatalogIssue? issue) =>
        issue?.Kind
            is CatalogIssueKind.CacheStale
                or CatalogIssueKind.CacheReadFailed
                or CatalogIssueKind.CacheInvalid;

    private static VoiceCatalogReasonCode MapReason(CatalogIssueKind issue) =>
        issue switch
        {
            CatalogIssueKind.CacheMissing => VoiceCatalogReasonCode.CacheMissing,
            CatalogIssueKind.CacheStale => VoiceCatalogReasonCode.CacheStale,
            CatalogIssueKind.RefreshQueueFailed => VoiceCatalogReasonCode.RefreshQueueFailed,
            CatalogIssueKind.RemoteEmpty => VoiceCatalogReasonCode.EmptyResponse,
            CatalogIssueKind.RemoteDownloadFailed => VoiceCatalogReasonCode.RemoteFailed,
            CatalogIssueKind.CacheWriteFailed => VoiceCatalogReasonCode.WriteFailed,
            CatalogIssueKind.EmbeddedMissing => VoiceCatalogReasonCode.NoUsableCatalog,
            CatalogIssueKind.Unexpected => VoiceCatalogReasonCode.WarmUpException,
            _ => VoiceCatalogReasonCode.SourceRejected,
        };

    private static VoiceCatalogSource EventSource(CatalogIssueKind issue, CatalogSource source) =>
        issue switch
        {
            CatalogIssueKind.CacheReadFailed or CatalogIssueKind.CacheInvalid =>
                VoiceCatalogSource.Cache,
            CatalogIssueKind.EmbeddedMissing
            or CatalogIssueKind.EmbeddedReadFailed
            or CatalogIssueKind.EmbeddedInvalid => VoiceCatalogSource.Embedded,
            CatalogIssueKind.Unexpected => VoiceCatalogSource.None,
            _ => MapSource(source),
        };

    private static VoiceCatalogSource MapSource(CatalogSource source) =>
        source switch
        {
            CatalogSource.Cache => VoiceCatalogSource.Cache,
            CatalogSource.Embedded => VoiceCatalogSource.Embedded,
            CatalogSource.Remote => VoiceCatalogSource.Remote,
            _ => VoiceCatalogSource.None,
        };

    private static string CatalogName(CatalogSource source) =>
        source switch
        {
            CatalogSource.Cache => "cache",
            CatalogSource.Embedded => "embedded",
            CatalogSource.Remote => "remote",
            _ => "none",
        };
}
