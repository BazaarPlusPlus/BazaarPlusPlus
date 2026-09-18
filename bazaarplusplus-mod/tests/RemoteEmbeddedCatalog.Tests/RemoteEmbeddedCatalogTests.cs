using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;
using Xunit;

namespace RemoteEmbeddedCatalog.Tests;

public sealed class RemoteEmbeddedCatalogTests
{
    [Fact]
    public async Task WarmAsync_PublishesFreshCacheWithoutReadingFallbacksOrSchedulingRemote()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var parser = new TextParser();
        var embedded = new EmbeddedSource("embedded");
        var cache = new CacheSource(new CatalogCacheDocument("cache", now.AddHours(-1)));
        var remote = new RemoteSource("remote");
        var scheduler = new RecordingScheduler();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            parser,
            embedded,
            cache,
            remote,
            new FixedClock(now),
            scheduler,
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        await catalog.WarmAsync();

        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("CACHE", snapshot.Value);
        Assert.Equal(CatalogSource.Cache, snapshot.Source);
        Assert.False(snapshot.IsStale);
        Assert.Equal(0, embedded.ReadCount);
        Assert.Equal(0, remote.DownloadCount);
        Assert.Empty(scheduler.Queued);
    }

    [Fact]
    public async Task WarmAsync_PublishesStaleCacheThenRefreshesFromRemoteInBackground()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var cache = new CacheSource(new CatalogCacheDocument("cache", now.AddHours(-21)));
        var remote = new RemoteSource("remote");
        var scheduler = new RecordingScheduler();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource("embedded"),
            cache,
            remote,
            new FixedClock(now),
            scheduler,
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        await catalog.WarmAsync();

        Assert.True(catalog.TryGet(out var stale));
        Assert.Equal("CACHE", stale.Value);
        Assert.True(stale.IsStale);
        Assert.Single(scheduler.Queued);
        Assert.Equal(0, remote.DownloadCount);

        await scheduler.Queued[0]();

        Assert.True(catalog.TryGet(out var refreshed));
        Assert.Equal("REMOTE", refreshed.Value);
        Assert.Equal(CatalogSource.Remote, refreshed.Source);
        Assert.False(refreshed.IsStale);
        Assert.Equal(["remote"], cache.Writes);
    }

    [Fact]
    public async Task WarmAsync_UsesEmbeddedWhenCacheIsMissingAndSchedulesRemote()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var embedded = new EmbeddedSource("embedded");
        var scheduler = new RecordingScheduler();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            embedded,
            new CacheSource(null),
            new RemoteSource("remote"),
            new FixedClock(now),
            scheduler,
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        await catalog.WarmAsync();

        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("EMBEDDED", snapshot.Value);
        Assert.Equal(CatalogSource.Embedded, snapshot.Source);
        Assert.Equal(CatalogIssueKind.CacheMissing, snapshot.Issue?.Kind);
        Assert.Equal(1, embedded.ReadCount);
        Assert.Single(scheduler.Queued);
    }

    [Fact]
    public async Task WarmAsync_ColdStartQueuesRemoteAndPublishesRecovery()
    {
        var scheduler = new RecordingScheduler();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            new RemoteSource("remote"),
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            scheduler,
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        await catalog.WarmAsync();

        Assert.False(catalog.TryGet(out _));
        Assert.Single(scheduler.Queued);

        await scheduler.Queued[0]();

        Assert.True(catalog.TryGet(out var recovered));
        Assert.Equal("REMOTE", recovered.Value);
        Assert.Equal(CatalogSource.Remote, recovered.Source);
    }

    [Fact]
    public async Task WarmAsync_ColdRefreshAlreadyQueuedDoesNotStartAnotherWarmFlight()
    {
        var cache = new CountingCacheSource();
        var embedded = new EmbeddedSource(null);
        var scheduler = new RecordingScheduler();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            embedded,
            cache,
            new RemoteSource("remote"),
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            scheduler,
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        await catalog.WarmAsync();
        await catalog.WarmAsync();

        Assert.Equal(1, cache.ReadCount);
        Assert.Equal(1, embedded.ReadCount);
        Assert.Single(scheduler.Queued);
    }

    [Fact]
    public async Task WarmAsync_CanceledBeforeBackgroundHandoffCannotStartRemoteRefresh()
    {
        using var cancellation = new CancellationTokenSource();
        var embedded = new BlockingEmbeddedSource();
        var remote = new RemoteSource("remote");
        var scheduler = new RecordingScheduler();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            embedded,
            new CacheSource(null),
            remote,
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            scheduler,
            new CancelOnQueuedObserver<string>(cancellation),
            TimeSpan.FromHours(20)
        );

        var warm = catalog.WarmAsync(cancellation.Token).AsTask();
        embedded.CompleteRead(null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => warm);
        await scheduler.Enqueued.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.Single(scheduler.Queued)();

        Assert.Equal(0, remote.DownloadCount);
        Assert.False(catalog.TryGet(out _));
    }

    [Fact]
    public async Task WarmAsync_CoalescesConcurrentCallers()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var cache = new BlockingCacheSource();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource("embedded"),
            cache,
            new RemoteSource("remote"),
            new FixedClock(now),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        var first = catalog.WarmAsync().AsTask();
        var second = catalog.WarmAsync().AsTask();

        Assert.Equal(1, cache.ReadCount);
        cache.CompleteRead(new CatalogCacheDocument("cache", now));
        await Task.WhenAll(first, second);

        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("CACHE", snapshot.Value);
    }

    [Fact]
    public async Task RefreshAsync_CoalescesConcurrentCallers()
    {
        var remote = new BlockingRemoteSource();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            remote,
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        var first = catalog.RefreshAsync().AsTask();
        var second = catalog.RefreshAsync().AsTask();

        Assert.Equal(1, remote.DownloadCount);
        remote.CompleteDownload("remote");

        Assert.True((await first).Succeeded);
        Assert.True((await second).Succeeded);
    }

    [Fact]
    public async Task RefreshAsync_CanceledStarterDoesNotCancelAnActiveWaiter()
    {
        var remote = new BlockingRemoteSource();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            remote,
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );
        using var firstCancellation = new CancellationTokenSource();

        var first = catalog.RefreshAsync(firstCancellation.Token).AsTask();
        var second = catalog.RefreshAsync().AsTask();
        firstCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        remote.CompleteDownload("remote");
        Assert.True((await second).Succeeded);
        Assert.Equal(1, remote.DownloadCount);
        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("REMOTE", snapshot.Value);
    }

    [Fact]
    public async Task WarmAsync_DoesNotOverwriteRemotePublishedByConcurrentRefresh()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var cache = new BlockingCacheSource();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource("embedded"),
            cache,
            new RemoteSource("remote"),
            new FixedClock(now),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        var warm = catalog.WarmAsync().AsTask();
        Assert.True((await catalog.RefreshAsync()).Succeeded);
        cache.CompleteRead(new CatalogCacheDocument("old-cache", now));
        await warm;

        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("REMOTE", snapshot.Value);
        Assert.Equal(CatalogSource.Remote, snapshot.Source);
    }

    [Fact]
    public async Task WarmAsync_InvalidCacheFallsBackToEmbedded()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new RejectingParser("bad"),
            new EmbeddedSource("embedded"),
            new CacheSource(new CatalogCacheDocument("bad", now)),
            new RemoteSource("remote"),
            new FixedClock(now),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        await catalog.WarmAsync();

        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("EMBEDDED", snapshot.Value);
        Assert.Equal(CatalogIssueKind.CacheInvalid, snapshot.Issue?.Kind);
    }

    [Fact]
    public async Task RefreshAsync_CacheWriteFailureStillPublishesDegradedRemoteSnapshot()
    {
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new ThrowingWriteCache(),
            new RemoteSource("remote"),
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        var result = await catalog.RefreshAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(CatalogRefreshOutcome.PublishedDegraded, result.Outcome);
        Assert.Equal(CatalogIssueKind.CacheWriteFailed, result.Issue?.Kind);
        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("REMOTE", snapshot.Value);
    }

    [Fact]
    public async Task RefreshAsync_DownloadFailurePreservesLastKnownGoodSnapshot()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(new CatalogCacheDocument("cache", now)),
            new ThrowingRemoteSource(),
            new FixedClock(now),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );
        await catalog.WarmAsync();

        var result = await catalog.RefreshAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(CatalogIssueKind.RemoteDownloadFailed, result.Issue?.Kind);
        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("CACHE", snapshot.Value);
        Assert.Equal(CatalogSource.Cache, snapshot.Source);
    }

    [Fact]
    public async Task RefreshAsync_InvalidRemotePreservesLastKnownGoodSnapshot()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new RejectingParser("bad"),
            new EmbeddedSource(null),
            new CacheSource(new CatalogCacheDocument("cache", now)),
            new RemoteSource("bad"),
            new FixedClock(now),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );
        await catalog.WarmAsync();

        var result = await catalog.RefreshAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(CatalogIssueKind.RemoteInvalid, result.Issue?.Kind);
        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("CACHE", snapshot.Value);
        Assert.Equal(CatalogSource.Cache, snapshot.Source);
    }

    [Fact]
    public async Task WarmAsync_ColdRefreshFailureRearmsLaterWarmUp()
    {
        var scheduler = new RecordingScheduler();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            new RemoteSource(null),
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            scheduler,
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        await catalog.WarmAsync();
        await Assert.Single(scheduler.Queued)();
        await catalog.WarmAsync();

        Assert.Equal(2, scheduler.Queued.Count);
        Assert.False(catalog.TryGet(out _));
    }

    [Fact]
    public async Task WarmAsync_ImmediateColdRefreshFailureDoesNotRaceRearm()
    {
        var scheduler = new ImmediateScheduler();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            new RemoteSource(null),
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            scheduler,
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        await catalog.WarmAsync();
        await catalog.WarmAsync();

        Assert.Equal(2, scheduler.QueueCount);
        Assert.False(catalog.TryGet(out _));
    }

    [Fact]
    public async Task WarmAsync_QueueFailureDoesNotBrickColdStart()
    {
        var scheduler = new ThrowingScheduler();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            new RemoteSource("remote"),
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            scheduler,
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        await catalog.WarmAsync();
        await catalog.WarmAsync();

        Assert.Equal(2, scheduler.QueueCount);
        Assert.False(catalog.TryGet(out _));
    }

    [Fact]
    public async Task RefreshAsync_CancellationBeforePublishLeavesSnapshotUnchanged()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var remote = new BlockingRemoteSource();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(new CatalogCacheDocument("cache", now)),
            remote,
            new FixedClock(now),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );
        using var cancellation = new CancellationTokenSource();
        await catalog.WarmAsync();

        var refresh = catalog.RefreshAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        remote.CompleteDownload("remote");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("CACHE", snapshot.Value);
        Assert.Equal(CatalogSource.Cache, snapshot.Source);
    }

    [Fact]
    public async Task RefreshAsync_AbandonedFlightCannotOverwriteItsReplacement()
    {
        var now = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        var remote = new SequencedBlockingRemoteSource();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(new CatalogCacheDocument("cache", now)),
            remote,
            new FixedClock(now),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );
        using var cancellation = new CancellationTokenSource();
        await catalog.WarmAsync();

        var abandoned = catalog.RefreshAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        var replacement = catalog.RefreshAsync().AsTask();
        remote.CompleteDownload(1, "new");
        Assert.True((await replacement).Succeeded);
        remote.CompleteDownload(0, "old");

        Assert.True(catalog.TryGet(out var snapshot));
        Assert.Equal("NEW", snapshot.Value);
        Assert.Equal(CatalogSource.Remote, snapshot.Source);
    }

    [Fact]
    public async Task Dispose_PreventsLateRemotePublish()
    {
        var remote = new BlockingRemoteSource();
        var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            remote,
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        var refresh = catalog.RefreshAsync().AsTask();
        catalog.Dispose();
        remote.CompleteDownload("remote");

        Assert.False((await refresh).Succeeded);
        Assert.False(catalog.TryGet(out _));
    }

    [Fact]
    public async Task Dispose_CancelsTheInFlightRemoteSource()
    {
        var remote = new CancellationAwareRemoteSource();
        var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            remote,
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        var refresh = catalog.RefreshAsync().AsTask();
        await remote.Started;
        catalog.Dispose();

        await remote.Canceled.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False((await refresh).Succeeded);
        Assert.False(catalog.TryGet(out _));
    }

    [Fact]
    public async Task RefreshAsync_UnexpectedClockFailureCompletesWithTypedResult()
    {
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            new RemoteSource("remote"),
            new ThrowingClock(),
            new RecordingScheduler(),
            new RecordingObserver<string>(),
            TimeSpan.FromHours(20)
        );

        var result = await catalog.RefreshAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(result.Succeeded);
        Assert.Equal(CatalogIssueKind.Unexpected, result.Issue?.Kind);
        Assert.IsType<InvalidOperationException>(result.Issue?.Exception);
        Assert.False(catalog.TryGet(out _));
    }

    [Fact]
    public async Task TryGet_DoesNotWaitForFeatureObserver()
    {
        var observer = new BlockingRefreshObserver<string>();
        using var catalog = new RemoteEmbeddedCatalog<string>(
            new TextParser(),
            new EmbeddedSource(null),
            new CacheSource(null),
            new RemoteSource("remote"),
            new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc)),
            new RecordingScheduler(),
            observer,
            TimeSpan.FromHours(20)
        );

        var refresh = Task.Run(async () => await catalog.RefreshAsync());
        await observer.Entered.WaitAsync(TimeSpan.FromSeconds(1));

        try
        {
            var lookup = await Task.Run(() =>
                {
                    var found = catalog.TryGet(out var snapshot);
                    return (found, snapshot);
                })
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(lookup.found);
            Assert.Equal("REMOTE", lookup.snapshot.Value);
        }
        finally
        {
            observer.Release();
        }
        Assert.True((await refresh).Succeeded);
    }

    private sealed class TextParser : ICatalogParser<string>
    {
        public CatalogParseResult<string> Parse(string document, CatalogSource source) =>
            CatalogParseResult<string>.Success(document.ToUpperInvariant());
    }

    private sealed class RejectingParser(string rejectedDocument) : ICatalogParser<string>
    {
        public CatalogParseResult<string> Parse(string document, CatalogSource source) =>
            document == rejectedDocument
                ? CatalogParseResult<string>.Failure("rejected")
                : CatalogParseResult<string>.Success(document.ToUpperInvariant());
    }

    private sealed class EmbeddedSource(string? document) : IEmbeddedCatalogSource
    {
        public int ReadCount { get; private set; }

        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return ValueTask.FromResult(document);
        }
    }

    private sealed class BlockingEmbeddedSource : IEmbeddedCatalogSource
    {
        private readonly TaskCompletionSource<string?> _read = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken) => new(_read.Task);

        internal void CompleteRead(string? document) => _read.SetResult(document);
    }

    private sealed class CacheSource(CatalogCacheDocument? document) : ILocalCatalogCache
    {
        public List<string> Writes { get; } = [];

        public ValueTask<CatalogCacheDocument?> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(document);

        public ValueTask WriteAsync(string document, CancellationToken cancellationToken)
        {
            Writes.Add(document);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingCacheSource : ILocalCatalogCache
    {
        public int ReadCount { get; private set; }

        public ValueTask<CatalogCacheDocument?> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return ValueTask.FromResult<CatalogCacheDocument?>(null);
        }

        public ValueTask WriteAsync(string document, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class BlockingCacheSource : ILocalCatalogCache
    {
        private readonly TaskCompletionSource<CatalogCacheDocument?> _read = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int ReadCount { get; private set; }

        public ValueTask<CatalogCacheDocument?> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return new ValueTask<CatalogCacheDocument?>(_read.Task);
        }

        public ValueTask WriteAsync(string document, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public void CompleteRead(CatalogCacheDocument? document) => _read.SetResult(document);
    }

    private sealed class ThrowingWriteCache : ILocalCatalogCache
    {
        public ValueTask<CatalogCacheDocument?> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<CatalogCacheDocument?>(null);

        public ValueTask WriteAsync(string document, CancellationToken cancellationToken) =>
            ValueTask.FromException(new IOException("cache write failed"));
    }

    private sealed class RemoteSource(string? document) : IRemoteCatalogSource
    {
        public int DownloadCount { get; private set; }

        public ValueTask<string?> DownloadAsync(CancellationToken cancellationToken)
        {
            DownloadCount++;
            return ValueTask.FromResult(document);
        }
    }

    private sealed class ThrowingRemoteSource : IRemoteCatalogSource
    {
        public ValueTask<string?> DownloadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<string?>(new HttpRequestException("download failed"));
    }

    private sealed class BlockingRemoteSource : IRemoteCatalogSource
    {
        private readonly TaskCompletionSource<string?> _download = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public int DownloadCount { get; private set; }

        public ValueTask<string?> DownloadAsync(CancellationToken cancellationToken)
        {
            DownloadCount++;
            return new ValueTask<string?>(_download.Task);
        }

        public void CompleteDownload(string? document) => _download.SetResult(document);
    }

    private sealed class SequencedBlockingRemoteSource : IRemoteCatalogSource
    {
        private readonly List<TaskCompletionSource<string?>> _downloads = [];

        public ValueTask<string?> DownloadAsync(CancellationToken cancellationToken)
        {
            // Deliberately ignore cancellation and run continuations inline: completing a download
            // returns only after the catalog has had a chance to publish it.
            var download = new TaskCompletionSource<string?>();
            _downloads.Add(download);
            return new ValueTask<string?>(download.Task);
        }

        internal void CompleteDownload(int index, string document) =>
            _downloads[index].SetResult(document);
    }

    private sealed class CancellationAwareRemoteSource : IRemoteCatalogSource
    {
        private readonly TaskCompletionSource<string?> _download = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> _canceled = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task Started => _started.Task;
        internal Task Canceled => _canceled.Task;

        public ValueTask<string?> DownloadAsync(CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            cancellationToken.Register(() =>
            {
                _canceled.TrySetResult(true);
                _download.TrySetCanceled(cancellationToken);
            });
            return new ValueTask<string?>(_download.Task);
        }
    }

    private sealed class FixedClock(DateTime utcNow) : ICatalogClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class ThrowingClock : ICatalogClock
    {
        public DateTime UtcNow => throw new InvalidOperationException("clock failed");
    }

    private sealed class RecordingScheduler : ICatalogRefreshScheduler
    {
        private readonly TaskCompletionSource<bool> _enqueued = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public List<Func<Task>> Queued { get; } = [];

        internal Task Enqueued => _enqueued.Task;

        public void Queue(Func<Task> refresh)
        {
            Queued.Add(refresh);
            _enqueued.TrySetResult(true);
        }
    }

    private sealed class ThrowingScheduler : ICatalogRefreshScheduler
    {
        public int QueueCount { get; private set; }

        public void Queue(Func<Task> refresh)
        {
            QueueCount++;
            throw new InvalidOperationException("queue failed");
        }
    }

    private sealed class ImmediateScheduler : ICatalogRefreshScheduler
    {
        public int QueueCount { get; private set; }

        public void Queue(Func<Task> refresh)
        {
            QueueCount++;
            refresh().GetAwaiter().GetResult();
        }
    }

    private sealed class RecordingObserver<TSnapshot> : IRemoteEmbeddedCatalogObserver<TSnapshot>
    {
        public void OnWarmStarted() { }

        public void OnInitialLoad(CatalogInitialLoadResult<TSnapshot> result) { }

        public void OnRefreshQueued(CatalogIssue reason) { }

        public void OnRefreshCompleted(
            CatalogRefreshTrigger trigger,
            CatalogRefreshResult<TSnapshot> result
        ) { }
    }

    private sealed class BlockingRefreshObserver<TSnapshot>
        : IRemoteEmbeddedCatalogObserver<TSnapshot>
    {
        private readonly TaskCompletionSource<bool> _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task Entered => _entered.Task;

        internal void Release() => _release.TrySetResult(true);

        public void OnWarmStarted() { }

        public void OnInitialLoad(CatalogInitialLoadResult<TSnapshot> result) { }

        public void OnRefreshQueued(CatalogIssue reason) { }

        public void OnRefreshCompleted(
            CatalogRefreshTrigger trigger,
            CatalogRefreshResult<TSnapshot> result
        )
        {
            _entered.TrySetResult(true);
            _release.Task.GetAwaiter().GetResult();
        }
    }

    private sealed class CancelOnQueuedObserver<TSnapshot>(CancellationTokenSource cancellation)
        : IRemoteEmbeddedCatalogObserver<TSnapshot>
    {
        public void OnWarmStarted() { }

        public void OnInitialLoad(CatalogInitialLoadResult<TSnapshot> result) { }

        public void OnRefreshQueued(CatalogIssue reason) => cancellation.Cancel();

        public void OnRefreshCompleted(
            CatalogRefreshTrigger trigger,
            CatalogRefreshResult<TSnapshot> result
        ) { }
    }
}
