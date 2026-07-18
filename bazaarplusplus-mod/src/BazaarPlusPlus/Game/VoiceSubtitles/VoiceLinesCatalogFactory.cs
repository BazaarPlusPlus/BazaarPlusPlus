#nullable enable
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
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

    internal static IRemoteEmbeddedCatalog<VoiceLine[]> Create(string gameRootPath)
    {
        var cache = new FileCatalogCache(BuildCacheFilePath(gameRootPath));
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

    internal static string BuildCacheFilePath(string gameRootPath) =>
        Path.Combine(gameRootPath, "BazaarPlusPlusV4", CacheFileName);
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
        BppLog.DebugEvent(VoiceCatalogLogEvents.CatalogStarted, static () => []);

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
                    VoiceCatalogLogEvents.CatalogReady,
                    VoiceCatalogLogEvents.CatalogReadySource.Bind(MapSource(snapshot.Source)),
                    VoiceCatalogLogEvents.CatalogReadyLineCount.Bind(snapshot.Value.Length)
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
            VoiceCatalogLogEvents.CatalogRefreshStarted,
            () =>
                [
                    VoiceCatalogLogEvents.CatalogRefreshStartedReasonCode.Bind(
                        MapReason(reason.Kind)
                    ),
                    VoiceCatalogLogEvents.CatalogRefreshStartedEndpoint.Bind(
                        VoiceCatalogEndpoint.VoiceCatalog
                    ),
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
                    VoiceCatalogLogEvents.CatalogCacheDegraded,
                    cacheIssue.Exception!,
                    VoiceCatalogLogEvents.CatalogCacheDegradedReasonCode.Bind(
                        VoiceCatalogReasonCode.WriteFailed
                    )
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
                    VoiceCatalogLogEvents.CatalogDegraded,
                    VoiceCatalogLogEvents.CatalogDegradedReasonCode.Bind(
                        recovered.Value.ReasonCode
                    ),
                    VoiceCatalogLogEvents.CatalogDegradedSource.Bind(recovered.Value.Source)
                );
                BppLog.InfoEvent(
                    VoiceCatalogLogEvents.CatalogRecovered,
                    VoiceCatalogLogEvents.CatalogRecoveredReasonCode.Bind(
                        recovered.Value.ReasonCode
                    ),
                    VoiceCatalogLogEvents.CatalogRecoveredSource.Bind(recovered.Value.Source),
                    VoiceCatalogLogEvents.CatalogRecoveredLineCount.Bind(snapshot.Value.Length)
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

        var fields = new[]
        {
            VoiceCatalogLogEvents.CatalogDegradedReasonCode.Bind(reason),
            VoiceCatalogLogEvents.CatalogDegradedSource.Bind(source),
            VoiceCatalogLogEvents.CatalogDegradedEndpoint.Bind(VoiceCatalogEndpoint.VoiceCatalog),
        };
        if (exception == null)
            BppLog.WarnEvent(VoiceCatalogLogEvents.CatalogDegraded, fields);
        else
            BppLog.WarnEvent(VoiceCatalogLogEvents.CatalogDegraded, exception, fields);
    }

    private static void EmitFailed(
        VoiceCatalogReasonCode reason,
        VoiceCatalogSource source,
        Exception? exception
    )
    {
        var fields = new[]
        {
            VoiceCatalogLogEvents.CatalogFailedReasonCode.Bind(reason),
            VoiceCatalogLogEvents.CatalogFailedSource.Bind(source),
        };
        if (exception == null)
            BppLog.ErrorEvent(VoiceCatalogLogEvents.CatalogFailed, fields);
        else
            BppLog.ErrorEvent(VoiceCatalogLogEvents.CatalogFailed, exception, fields);
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
