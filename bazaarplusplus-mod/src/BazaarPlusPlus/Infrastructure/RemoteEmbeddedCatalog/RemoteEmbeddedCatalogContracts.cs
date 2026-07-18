#nullable enable
namespace BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

internal interface IRemoteEmbeddedCatalog<TSnapshot> : IDisposable
{
    bool TryGet(out CatalogSnapshot<TSnapshot> snapshot);

    ValueTask WarmAsync(CancellationToken cancellationToken = default);

    ValueTask<CatalogRefreshResult<TSnapshot>> RefreshAsync(
        CancellationToken cancellationToken = default
    );
}

internal interface ICatalogParser<TSnapshot>
{
    CatalogParseResult<TSnapshot> Parse(string document, CatalogSource source);
}

internal interface IEmbeddedCatalogSource
{
    ValueTask<string?> ReadAsync(CancellationToken cancellationToken);
}

internal interface ILocalCatalogCache
{
    ValueTask<CatalogCacheDocument?> ReadAsync(CancellationToken cancellationToken);

    ValueTask WriteAsync(string document, CancellationToken cancellationToken);
}

internal interface IRemoteCatalogSource
{
    ValueTask<string?> DownloadAsync(CancellationToken cancellationToken);
}

internal interface ICatalogClock
{
    DateTime UtcNow { get; }
}

internal interface ICatalogRefreshScheduler
{
    void Queue(Func<Task> refresh);
}

internal interface IRemoteEmbeddedCatalogObserver<TSnapshot>
{
    void OnWarmStarted();

    void OnInitialLoad(CatalogInitialLoadResult<TSnapshot> result);

    void OnRefreshQueued(CatalogIssue reason);

    void OnRefreshCompleted(CatalogRefreshTrigger trigger, CatalogRefreshResult<TSnapshot> result);
}

internal enum CatalogSource
{
    Cache,
    Embedded,
    Remote,
}

internal enum CatalogIssueKind
{
    CacheMissing,
    CacheStale,
    CacheReadFailed,
    CacheInvalid,
    EmbeddedMissing,
    EmbeddedReadFailed,
    EmbeddedInvalid,
    RemoteEmpty,
    RemoteDownloadFailed,
    RemoteInvalid,
    CacheWriteFailed,
    RefreshQueueFailed,
    Disposed,
    Unexpected,
}

internal enum CatalogRefreshTrigger
{
    Background,
    Manual,
}

internal enum CatalogRefreshOutcome
{
    Published,
    PublishedDegraded,
    Failed,
}

internal readonly record struct CatalogCacheDocument(string Document, DateTime LastWriteUtc);

internal readonly record struct CatalogIssue(
    CatalogIssueKind Kind,
    Exception? Exception = null,
    string? Detail = null
);

internal readonly record struct CatalogSnapshot<TSnapshot>(
    TSnapshot Value,
    CatalogSource Source,
    DateTime PublishedAtUtc,
    bool IsStale,
    CatalogIssue? Issue
);

internal readonly record struct CatalogParseResult<TSnapshot>(
    bool Succeeded,
    TSnapshot? Value,
    string? Error
)
{
    internal static CatalogParseResult<TSnapshot> Success(TSnapshot value) =>
        new(true, value, null);

    internal static CatalogParseResult<TSnapshot> Failure(string? error = null) =>
        new(false, default, error);
}

internal readonly record struct CatalogInitialLoadResult<TSnapshot>(
    CatalogSnapshot<TSnapshot>? Snapshot,
    CatalogIssue? Issue
)
{
    internal static CatalogInitialLoadResult<TSnapshot> Published(
        CatalogSnapshot<TSnapshot> snapshot
    ) => new(snapshot, snapshot.Issue);

    internal static CatalogInitialLoadResult<TSnapshot> Unavailable(CatalogIssue issue) =>
        new(null, issue);
}

internal readonly record struct CatalogRefreshResult<TSnapshot>(
    CatalogRefreshOutcome Outcome,
    CatalogSnapshot<TSnapshot>? Snapshot,
    CatalogIssue? Issue
)
{
    internal bool Succeeded => Outcome != CatalogRefreshOutcome.Failed;

    internal static CatalogRefreshResult<TSnapshot> Published(
        CatalogSnapshot<TSnapshot> snapshot,
        bool degraded
    ) =>
        new(
            degraded ? CatalogRefreshOutcome.PublishedDegraded : CatalogRefreshOutcome.Published,
            snapshot,
            snapshot.Issue
        );

    internal static CatalogRefreshResult<TSnapshot> Failure(CatalogIssue issue) =>
        new(CatalogRefreshOutcome.Failed, null, issue);
}
