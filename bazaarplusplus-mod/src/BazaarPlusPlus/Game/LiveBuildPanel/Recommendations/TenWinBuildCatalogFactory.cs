#nullable enable
using System.Reflection;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using BazaarPlusPlus.ModApi.Http;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

internal static class TenWinBuildCatalogFactory
{
    internal const string EmbeddedResourceName =
        "BazaarPlusPlus.Data.BuildRecommendations.tenwin_builds.json";
    private const string RemoteUrl =
        "https://bpp-metrics.bazaarplusplus.com/analyzer-v4/mod/tenwin_builds.json";
    private const string CacheFileName = "tenwin_builds.json";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(20);
    private static readonly HttpClient HttpClient = BppHttpClientFactory.Create(
        productVersion: BppPluginVersion.Current,
        userAgentSuffix: "TenWinBuildRepository",
        timeout: TimeSpan.FromSeconds(10)
    );

    internal static IRemoteEmbeddedCatalog<TenWinBuildCorpus> Create(string gameRootPath)
    {
        var cache = new FileCatalogCache(BuildCacheFilePath(gameRootPath));
        return new RemoteEmbeddedCatalog<TenWinBuildCorpus>(
            new TenWinBuildCatalogParser(),
            new AssemblyResourceCatalogSource(
                Assembly.GetExecutingAssembly(),
                EmbeddedResourceName
            ),
            cache,
            new HttpRemoteCatalogSource(HttpClient, RemoteUrl),
            SystemCatalogClock.Instance,
            ThreadPoolCatalogRefreshScheduler.Instance,
            new TenWinBuildCatalogObserver(cache.FilePath),
            CacheDuration
        );
    }

    internal static string BuildCacheFilePath(string gameRootPath) =>
        Path.Combine(gameRootPath, "BazaarPlusPlusV4", CacheFileName);
}

internal sealed class TenWinBuildCatalogParser : ICatalogParser<TenWinBuildCorpus>
{
    public CatalogParseResult<TenWinBuildCorpus> Parse(string document, CatalogSource source)
    {
        var corpus = TenWinBuildCorpus.Parse(document);
        return corpus == null
            ? CatalogParseResult<TenWinBuildCorpus>.Failure("invalid_response")
            : CatalogParseResult<TenWinBuildCorpus>.Success(corpus);
    }
}

internal sealed class TenWinBuildCatalogObserver : IRemoteEmbeddedCatalogObserver<TenWinBuildCorpus>
{
    private readonly object _sync = new();
    private readonly string _cachePath;
    private readonly BuildRecommendationCorpusLogState _logState = new();
    private LiveBuildCorpusSource _currentSource = LiveBuildCorpusSource.Unavailable;
    private int _currentBuildCount;

    internal TenWinBuildCatalogObserver(string cachePath)
    {
        _cachePath = cachePath;
    }

    public void OnWarmStarted() => _logState.ReportWarmupStarted();

    public void OnInitialLoad(CatalogInitialLoadResult<TenWinBuildCorpus> result)
    {
        if (result.Snapshot is { } snapshot)
        {
            var source = MapSource(snapshot.Source);
            lock (_sync)
            {
                _currentSource = source;
                _currentBuildCount = snapshot.Value.BuildCount;
            }

            if (snapshot.Source == CatalogSource.Cache)
            {
                _logState.ReportCacheLoaded(
                    snapshot.Value.BuildCount,
                    snapshot.IsStale,
                    _cachePath
                );
            }

            if (snapshot.Issue is { } issue)
            {
                _logState.ReportDegraded(
                    new CorpusDegradation(
                        MapInitialReason(issue.Kind),
                        source,
                        snapshot.Value.BuildCount,
                        snapshot.IsStale,
                        CachePathFor(issue.Kind),
                        issue.Exception
                    )
                );
            }
            else
            {
                _logState.ReportReady(source, snapshot.Value.BuildCount);
            }
            return;
        }

        var unavailable = result.Issue ?? new CatalogIssue(CatalogIssueKind.Unexpected);
        _logState.ReportDegraded(
            new CorpusDegradation(
                MapInitialReason(unavailable.Kind),
                LiveBuildCorpusSource.Unavailable,
                0,
                Expired: false,
                CachePathFor(unavailable.Kind),
                unavailable.Exception
            )
        );
    }

    public void OnRefreshQueued(CatalogIssue reason) =>
        _logState.ReportRefreshQueued(MapInitialReason(reason.Kind));

    public void OnRefreshCompleted(
        CatalogRefreshTrigger trigger,
        CatalogRefreshResult<TenWinBuildCorpus> result
    )
    {
        if (result.Succeeded && result.Snapshot is { } snapshot)
        {
            lock (_sync)
            {
                _currentSource = LiveBuildCorpusSource.Remote;
                _currentBuildCount = snapshot.Value.BuildCount;
            }

            _logState.ReportRemoteLoaded(snapshot.Value.BuildCount);
            if (snapshot.Issue is { Kind: CatalogIssueKind.CacheWriteFailed } cacheIssue)
                _logState.ReportCacheWriteDegraded(_cachePath, cacheIssue.Exception!);
            else
                _logState.ReportCacheWriteRecovered();

            if (trigger == CatalogRefreshTrigger.Background)
            {
                _logState.ReportRecovered(LiveBuildCorpusSource.Remote, snapshot.Value.BuildCount);
            }
            else
            {
                _logState.ResetDegradedSilently();
            }
            return;
        }

        if (trigger != CatalogRefreshTrigger.Background)
            return;

        LiveBuildCorpusSource source;
        int buildCount;
        lock (_sync)
        {
            source = _currentSource;
            buildCount = _currentBuildCount;
        }
        var issue = result.Issue ?? new CatalogIssue(CatalogIssueKind.Unexpected);
        _logState.ReportDegraded(
            new CorpusDegradation(
                issue.Kind == CatalogIssueKind.RefreshQueueFailed
                    ? LiveBuildCorpusReasonCode.RefreshQueueFailed
                    : LiveBuildCorpusReasonCode.RemoteRefreshFailed,
                source,
                buildCount,
                Expired: false,
                CachePath: null,
                issue.Exception
            )
        );
    }

    private string? CachePathFor(CatalogIssueKind issue) =>
        issue
            is CatalogIssueKind.CacheMissing
                or CatalogIssueKind.CacheStale
                or CatalogIssueKind.CacheReadFailed
                or CatalogIssueKind.CacheInvalid
            ? _cachePath
            : null;

    private static LiveBuildCorpusSource MapSource(CatalogSource source) =>
        source switch
        {
            CatalogSource.Cache => LiveBuildCorpusSource.Cache,
            CatalogSource.Embedded => LiveBuildCorpusSource.Embedded,
            CatalogSource.Remote => LiveBuildCorpusSource.Remote,
            _ => LiveBuildCorpusSource.Unavailable,
        };

    private static LiveBuildCorpusReasonCode MapInitialReason(CatalogIssueKind issue) =>
        issue switch
        {
            CatalogIssueKind.CacheStale => LiveBuildCorpusReasonCode.StaleCache,
            CatalogIssueKind.CacheMissing => LiveBuildCorpusReasonCode.EmbeddedFallback,
            CatalogIssueKind.CacheReadFailed => LiveBuildCorpusReasonCode.CacheReadFailed,
            CatalogIssueKind.CacheInvalid => LiveBuildCorpusReasonCode.CacheInvalid,
            CatalogIssueKind.EmbeddedMissing => LiveBuildCorpusReasonCode.EmbeddedMissing,
            CatalogIssueKind.EmbeddedInvalid or CatalogIssueKind.EmbeddedReadFailed =>
                LiveBuildCorpusReasonCode.EmbeddedInvalid,
            CatalogIssueKind.RefreshQueueFailed => LiveBuildCorpusReasonCode.RefreshQueueFailed,
            _ => LiveBuildCorpusReasonCode.WarmupFailed,
        };
}
