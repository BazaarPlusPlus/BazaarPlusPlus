#nullable enable
namespace BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

public interface IRemoteEmbeddedCatalog<TSnapshot> : IDisposable
{
    bool TryGet(out CatalogSnapshot<TSnapshot> snapshot);

    ValueTask WarmAsync(CancellationToken cancellationToken = default);

    ValueTask<CatalogRefreshResult<TSnapshot>> RefreshAsync(
        CancellationToken cancellationToken = default
    );
}

public interface ICatalogParser<TSnapshot>
{
    CatalogParseResult<TSnapshot> Parse(string document, CatalogSource source);
}

public interface IEmbeddedCatalogSource
{
    ValueTask<string?> ReadAsync(CancellationToken cancellationToken);
}

public interface ILocalCatalogCache
{
    ValueTask<CatalogCacheDocument?> ReadAsync(CancellationToken cancellationToken);

    ValueTask WriteAsync(string document, CancellationToken cancellationToken);
}

public interface IRemoteCatalogSource
{
    ValueTask<string?> DownloadAsync(CancellationToken cancellationToken);
}

public interface ICatalogClock
{
    DateTime UtcNow { get; }
}

public interface ICatalogRefreshScheduler
{
    void Queue(Func<Task> refresh);
}

public interface IRemoteEmbeddedCatalogObserver<TSnapshot>
{
    void OnWarmStarted();

    void OnInitialLoad(CatalogInitialLoadResult<TSnapshot> result);

    void OnRefreshQueued(CatalogIssue reason);

    void OnRefreshCompleted(CatalogRefreshTrigger trigger, CatalogRefreshResult<TSnapshot> result);
}

public enum CatalogSource
{
    Cache,
    Embedded,
    Remote,
}

public enum CatalogIssueKind
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

public enum CatalogRefreshTrigger
{
    Background,
    Manual,
}

public enum CatalogRefreshOutcome
{
    Published,
    PublishedDegraded,
    Failed,
}

public readonly record struct CatalogCacheDocument(string Document, DateTime LastWriteUtc);

public readonly record struct CatalogIssue(
    CatalogIssueKind Kind,
    Exception? Exception = null,
    string? Detail = null
);

public readonly record struct CatalogSnapshot<TSnapshot>(
    TSnapshot Value,
    CatalogSource Source,
    DateTime PublishedAtUtc,
    bool IsStale,
    CatalogIssue? Issue
);

public readonly record struct CatalogParseResult<TSnapshot>(
    bool Succeeded,
    TSnapshot? Value,
    string? Error
)
{
    public static CatalogParseResult<TSnapshot> Success(TSnapshot value) => new(true, value, null);

    public static CatalogParseResult<TSnapshot> Failure(string? error = null) =>
        new(false, default, error);
}

public readonly record struct CatalogInitialLoadResult<TSnapshot>(
    CatalogSnapshot<TSnapshot>? Snapshot,
    CatalogIssue? Issue
)
{
    public static CatalogInitialLoadResult<TSnapshot> Published(
        CatalogSnapshot<TSnapshot> snapshot
    ) => new(snapshot, snapshot.Issue);

    public static CatalogInitialLoadResult<TSnapshot> Unavailable(CatalogIssue issue) =>
        new(null, issue);
}

public readonly record struct CatalogRefreshResult<TSnapshot>(
    CatalogRefreshOutcome Outcome,
    CatalogSnapshot<TSnapshot>? Snapshot,
    CatalogIssue? Issue
)
{
    public bool Succeeded => Outcome != CatalogRefreshOutcome.Failed;

    public static CatalogRefreshResult<TSnapshot> Published(
        CatalogSnapshot<TSnapshot> snapshot,
        bool degraded
    ) =>
        new(
            degraded ? CatalogRefreshOutcome.PublishedDegraded : CatalogRefreshOutcome.Published,
            snapshot,
            snapshot.Issue
        );

    public static CatalogRefreshResult<TSnapshot> Failure(CatalogIssue issue) =>
        new(CatalogRefreshOutcome.Failed, null, issue);
}
