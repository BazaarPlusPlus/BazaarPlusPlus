#nullable enable
namespace BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

internal sealed class RemoteEmbeddedCatalog<TSnapshot> : IRemoteEmbeddedCatalog<TSnapshot>
{
    private readonly object _sync = new();
    private readonly object _observerSync = new();
    private readonly SemaphoreSlim _remotePublishGate = new(1, 1);
    private readonly ICatalogParser<TSnapshot> _parser;
    private readonly IEmbeddedCatalogSource _embedded;
    private readonly ILocalCatalogCache _cache;
    private readonly IRemoteCatalogSource _remote;
    private readonly ICatalogClock _clock;
    private readonly ICatalogRefreshScheduler _scheduler;
    private readonly IRemoteEmbeddedCatalogObserver<TSnapshot> _observer;
    private readonly TimeSpan _cacheDuration;
    private CatalogSnapshot<TSnapshot>? _snapshot;
    private WarmFlight? _warmFlight;
    private RefreshFlight? _refreshFlight;
    private int _nextBackgroundRefreshTicket;
    private int? _scheduledBackgroundRefreshTicket;
    private bool _warmCompleted;
    private bool _disposed;
    private int _generation;
    private int _publicationVersion;

    internal RemoteEmbeddedCatalog(
        ICatalogParser<TSnapshot> parser,
        IEmbeddedCatalogSource embedded,
        ILocalCatalogCache cache,
        IRemoteCatalogSource remote,
        ICatalogClock clock,
        ICatalogRefreshScheduler scheduler,
        IRemoteEmbeddedCatalogObserver<TSnapshot> observer,
        TimeSpan cacheDuration
    )
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _embedded = embedded ?? throw new ArgumentNullException(nameof(embedded));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _remote = remote ?? throw new ArgumentNullException(nameof(remote));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _cacheDuration = cacheDuration;
    }

    public bool TryGet(out CatalogSnapshot<TSnapshot> snapshot)
    {
        lock (_sync)
        {
            if (!_disposed && _snapshot.HasValue)
            {
                snapshot = _snapshot.Value;
                return true;
            }
        }

        snapshot = default;
        return false;
    }

    public ValueTask WarmAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WarmFlight flight;
        var startFlight = false;
        int generation = 0;
        int publicationVersion = 0;
        lock (_sync)
        {
            if (_disposed || _warmCompleted)
                return default;

            flight = _warmFlight!;
            if (flight == null)
            {
                flight = new WarmFlight();
                _warmFlight = flight;
                generation = _generation;
                publicationVersion = _publicationVersion;
                startFlight = true;
            }
            flight.WaiterCount++;
        }

        if (startFlight)
        {
            ObserveIfCurrent(generation, static observer => observer.OnWarmStarted());
            _ = CompleteWarmAsync(flight, generation, publicationVersion);
        }
        return new ValueTask(WaitForWarmCallerAsync(flight, cancellationToken));
    }

    public ValueTask<CatalogRefreshResult<TSnapshot>> RefreshAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return BeginRefresh(CatalogRefreshTrigger.Manual, cancellationToken);
    }

    public void Dispose()
    {
        WarmFlight? warmFlight;
        RefreshFlight? refreshFlight;
        var cancelWarm = false;
        var cancelRefresh = false;
        lock (_observerSync)
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _generation++;
                _snapshot = null;
                _warmCompleted = false;
                _scheduledBackgroundRefreshTicket = null;
                warmFlight = _warmFlight;
                refreshFlight = _refreshFlight;
                if (warmFlight != null)
                    cancelWarm = AbandonFlight(warmFlight, disposedByOwner: true);
                if (refreshFlight != null)
                    cancelRefresh = AbandonFlight(refreshFlight, disposedByOwner: true);
                _warmFlight = null;
                _refreshFlight = null;
            }

            if (cancelWarm)
                CancelFlight(warmFlight!);
            if (cancelRefresh)
                CancelFlight(refreshFlight!);
        }
    }

    private async Task CompleteWarmAsync(WarmFlight flight, int generation, int publicationVersion)
    {
        try
        {
            await LoadInitialAsync(
                    flight,
                    generation,
                    publicationVersion,
                    flight.Cancellation.Token
                )
                .ConfigureAwait(false);
            flight.Completion.TrySetResult(true);
        }
        catch (OperationCanceledException)
        {
            if (WasDisposed(flight))
                flight.Completion.TrySetResult(true);
            else
                flight.Completion.TrySetCanceled();
        }
        catch (Exception ex)
        {
            ObserveIfCurrent(
                flight,
                generation,
                publicationVersion,
                observer =>
                    observer.OnInitialLoad(
                        CatalogInitialLoadResult<TSnapshot>.Unavailable(
                            new CatalogIssue(CatalogIssueKind.Unexpected, ex)
                        )
                    )
            );
            flight.Completion.TrySetResult(true);
        }
        finally
        {
            var disposeCancellation = false;
            lock (_sync)
            {
                if (ReferenceEquals(_warmFlight, flight))
                    _warmFlight = null;
                if (!_disposed && generation == _generation)
                {
                    _warmCompleted =
                        _snapshot.HasValue
                        || _scheduledBackgroundRefreshTicket.HasValue
                        || _refreshFlight != null;
                }
                flight.CompletionFinished = true;
                disposeCancellation = TryClaimCancellationDisposal(flight);
            }
            if (disposeCancellation)
                flight.Cancellation.Dispose();
        }
    }

    private async Task<bool> LoadInitialAsync(
        WarmFlight flight,
        int generation,
        int publicationVersion,
        CancellationToken cancellationToken
    )
    {
        var cacheIssue = new CatalogIssue(CatalogIssueKind.CacheMissing);
        CatalogCacheDocument? cacheDocument = null;
        try
        {
            cacheDocument = await _cache.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            cacheIssue = new CatalogIssue(CatalogIssueKind.CacheReadFailed, ex);
        }

        if (cacheDocument.HasValue)
        {
            var parsed = Parse(cacheDocument.Value.Document, CatalogSource.Cache);
            if (parsed.Succeeded && parsed.Value is not null)
            {
                var now = _clock.UtcNow;
                var isStale = now >= cacheDocument.Value.LastWriteUtc.Add(_cacheDuration);
                var snapshot = new CatalogSnapshot<TSnapshot>(
                    parsed.Value,
                    CatalogSource.Cache,
                    now,
                    isStale,
                    isStale ? new CatalogIssue(CatalogIssueKind.CacheStale) : null
                );
                if (
                    !PublishInitial(
                        flight,
                        snapshot,
                        generation,
                        publicationVersion,
                        cancellationToken
                    )
                )
                    return false;

                return !isStale
                    || ScheduleBackgroundRefresh(
                        flight,
                        snapshot.Issue!.Value,
                        generation,
                        publicationVersion
                    );
            }

            cacheIssue = new CatalogIssue(
                CatalogIssueKind.CacheInvalid,
                parsed.Exception,
                parsed.Error
            );
        }

        CatalogIssue embeddedIssue;
        string? embeddedDocument;
        try
        {
            embeddedDocument = await _embedded.ReadAsync(cancellationToken).ConfigureAwait(false);
            embeddedIssue = new CatalogIssue(CatalogIssueKind.EmbeddedMissing);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            embeddedDocument = null;
            embeddedIssue = new CatalogIssue(CatalogIssueKind.EmbeddedReadFailed, ex);
        }

        if (!string.IsNullOrWhiteSpace(embeddedDocument))
        {
            var parsed = Parse(embeddedDocument!, CatalogSource.Embedded);
            if (parsed.Succeeded && parsed.Value is not null)
            {
                var snapshot = new CatalogSnapshot<TSnapshot>(
                    parsed.Value,
                    CatalogSource.Embedded,
                    _clock.UtcNow,
                    IsStale: false,
                    cacheIssue
                );
                if (
                    !PublishInitial(
                        flight,
                        snapshot,
                        generation,
                        publicationVersion,
                        cancellationToken
                    )
                )
                    return false;

                ScheduleBackgroundRefresh(flight, cacheIssue, generation, publicationVersion);
                return true;
            }

            embeddedIssue = new CatalogIssue(
                CatalogIssueKind.EmbeddedInvalid,
                parsed.Exception,
                parsed.Error
            );
        }

        if (
            !ObserveUnavailable(
                flight,
                embeddedIssue,
                generation,
                publicationVersion,
                cancellationToken
            )
        )
            return false;
        return ScheduleBackgroundRefresh(flight, embeddedIssue, generation, publicationVersion);
    }

    private bool PublishInitial(
        WarmFlight flight,
        CatalogSnapshot<TSnapshot> snapshot,
        int generation,
        int publicationVersion,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                _disposed
                || flight.Abandoned
                || generation != _generation
                || publicationVersion != _publicationVersion
            )
                return false;

            _snapshot = snapshot;
        }

        return ObserveIfCurrent(
            flight,
            generation,
            publicationVersion,
            observer =>
                observer.OnInitialLoad(CatalogInitialLoadResult<TSnapshot>.Published(snapshot))
        );
    }

    private bool ObserveUnavailable(
        WarmFlight flight,
        CatalogIssue issue,
        int generation,
        int publicationVersion,
        CancellationToken cancellationToken
    )
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                _disposed
                || flight.Abandoned
                || generation != _generation
                || publicationVersion != _publicationVersion
            )
                return false;
        }

        return ObserveIfCurrent(
            flight,
            generation,
            publicationVersion,
            observer =>
                observer.OnInitialLoad(CatalogInitialLoadResult<TSnapshot>.Unavailable(issue))
        );
    }

    private bool ScheduleBackgroundRefresh(
        WarmFlight flight,
        CatalogIssue reason,
        int generation,
        int publicationVersion
    )
    {
        int ticket;
        lock (_sync)
        {
            if (
                _disposed
                || flight.Abandoned
                || generation != _generation
                || publicationVersion != _publicationVersion
            )
                return false;

            if (_scheduledBackgroundRefreshTicket.HasValue || _refreshFlight != null)
                return true;

            ticket = ++_nextBackgroundRefreshTicket;
            _scheduledBackgroundRefreshTicket = ticket;
        }

        try
        {
            // Announce the attempt before invoking the scheduler so even an inline scheduler
            // cannot report completion before the corresponding queued event.
            if (
                !ObserveIfCurrent(
                    flight,
                    generation,
                    publicationVersion,
                    observer => observer.OnRefreshQueued(reason)
                )
            )
            {
                MarkBackgroundRefreshDequeued(ticket, generation);
                return false;
            }
            _scheduler.Queue(async () =>
            {
                try
                {
                    var refresh = BeginRefresh(
                        CatalogRefreshTrigger.Background,
                        CancellationToken.None,
                        publicationVersion,
                        flight
                    );
                    MarkBackgroundRefreshDequeued(ticket, generation);
                    await refresh.ConfigureAwait(false);
                }
                finally
                {
                    MarkBackgroundRefreshDequeued(ticket, generation);
                }
            });
            return true;
        }
        catch (Exception ex)
        {
            MarkBackgroundRefreshDequeued(ticket, generation);
            var result = CatalogRefreshResult<TSnapshot>.Failure(
                new CatalogIssue(CatalogIssueKind.RefreshQueueFailed, ex)
            );
            ObserveIfCurrent(
                flight,
                generation,
                publicationVersion,
                observer => observer.OnRefreshCompleted(CatalogRefreshTrigger.Background, result)
            );
            return false;
        }
    }

    private ValueTask<CatalogRefreshResult<TSnapshot>> BeginRefresh(
        CatalogRefreshTrigger trigger,
        CancellationToken cancellationToken,
        int? expectedPublicationVersion = null,
        WarmFlight? originWarmFlight = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        RefreshFlight flight;
        var startFlight = false;
        int generation = 0;
        lock (_sync)
        {
            if (originWarmFlight?.Abandoned == true)
            {
                return new ValueTask<CatalogRefreshResult<TSnapshot>>(
                    CatalogRefreshResult<TSnapshot>.Failure(
                        new CatalogIssue(CatalogIssueKind.Disposed)
                    )
                );
            }

            if (
                expectedPublicationVersion.HasValue
                && expectedPublicationVersion.Value != _publicationVersion
            )
            {
                return new ValueTask<CatalogRefreshResult<TSnapshot>>(
                    CatalogRefreshResult<TSnapshot>.Failure(
                        new CatalogIssue(CatalogIssueKind.Unexpected, Detail: "refresh_superseded")
                    )
                );
            }

            if (_disposed)
            {
                return new ValueTask<CatalogRefreshResult<TSnapshot>>(
                    CatalogRefreshResult<TSnapshot>.Failure(
                        new CatalogIssue(CatalogIssueKind.Disposed)
                    )
                );
            }

            flight = _refreshFlight!;
            if (flight == null)
            {
                flight = new RefreshFlight();
                _refreshFlight = flight;
                generation = _generation;
                startFlight = true;
            }
            flight.WaiterCount++;
        }

        if (startFlight)
            _ = CompleteRefreshAsync(flight, trigger, generation);
        return new ValueTask<CatalogRefreshResult<TSnapshot>>(
            WaitForRefreshCallerAsync(flight, cancellationToken)
        );
    }

    private async Task CompleteRefreshAsync(
        RefreshFlight flight,
        CatalogRefreshTrigger trigger,
        int generation
    )
    {
        CatalogRefreshResult<TSnapshot>? result = null;
        try
        {
            result = await LoadRemoteAsync(flight, generation, flight.Cancellation.Token)
                .ConfigureAwait(false);
            if (!ObserveRefreshIfCurrent(flight, generation, trigger, result.Value))
            {
                result = CatalogRefreshResult<TSnapshot>.Failure(
                    new CatalogIssue(CatalogIssueKind.Disposed)
                );
            }
            flight.Completion.TrySetResult(result.Value);
        }
        catch (OperationCanceledException)
        {
            if (WasDisposed(flight))
            {
                result = CatalogRefreshResult<TSnapshot>.Failure(
                    new CatalogIssue(CatalogIssueKind.Disposed)
                );
                flight.Completion.TrySetResult(result.Value);
            }
            else
            {
                flight.Completion.TrySetCanceled();
            }
        }
        catch (Exception ex)
        {
            result = CatalogRefreshResult<TSnapshot>.Failure(
                new CatalogIssue(CatalogIssueKind.Unexpected, ex)
            );
            ObserveRefreshIfCurrent(flight, generation, trigger, result.Value);
            flight.Completion.TrySetResult(result.Value);
        }
        finally
        {
            var disposeCancellation = false;
            lock (_sync)
            {
                if (ReferenceEquals(_refreshFlight, flight))
                    _refreshFlight = null;
                if (!_disposed && generation == _generation && result.HasValue)
                {
                    if (result.Value.Succeeded)
                        _warmCompleted = true;
                    else if (!_snapshot.HasValue && !_scheduledBackgroundRefreshTicket.HasValue)
                        _warmCompleted = false;
                }
                flight.CompletionFinished = true;
                disposeCancellation = TryClaimCancellationDisposal(flight);
            }
            if (disposeCancellation)
                flight.Cancellation.Dispose();
        }
    }

    private async Task<CatalogRefreshResult<TSnapshot>> LoadRemoteAsync(
        RefreshFlight flight,
        int generation,
        CancellationToken cancellationToken
    )
    {
        string? document;
        try
        {
            document = await _remote.DownloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CatalogRefreshResult<TSnapshot>.Failure(
                new CatalogIssue(CatalogIssueKind.RemoteDownloadFailed, ex)
            );
        }

        if (string.IsNullOrWhiteSpace(document))
        {
            return CatalogRefreshResult<TSnapshot>.Failure(
                new CatalogIssue(CatalogIssueKind.RemoteEmpty)
            );
        }

        var parsed = Parse(document!, CatalogSource.Remote);
        if (!parsed.Succeeded || parsed.Value is null)
        {
            return CatalogRefreshResult<TSnapshot>.Failure(
                new CatalogIssue(CatalogIssueKind.RemoteInvalid, parsed.Exception, parsed.Error)
            );
        }

        await _remotePublishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || flight.Abandoned || generation != _generation)
                {
                    return CatalogRefreshResult<TSnapshot>.Failure(
                        new CatalogIssue(CatalogIssueKind.Disposed)
                    );
                }
            }

            CatalogIssue? cacheIssue = null;
            try
            {
                await _cache.WriteAsync(document!, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                cacheIssue = new CatalogIssue(CatalogIssueKind.CacheWriteFailed, ex);
            }

            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || flight.Abandoned || generation != _generation)
                {
                    return CatalogRefreshResult<TSnapshot>.Failure(
                        new CatalogIssue(CatalogIssueKind.Disposed)
                    );
                }

                var snapshot = new CatalogSnapshot<TSnapshot>(
                    parsed.Value,
                    CatalogSource.Remote,
                    _clock.UtcNow,
                    IsStale: false,
                    cacheIssue
                );
                _snapshot = snapshot;
                _publicationVersion++;
                return CatalogRefreshResult<TSnapshot>.Published(
                    snapshot,
                    degraded: cacheIssue.HasValue
                );
            }
        }
        finally
        {
            _remotePublishGate.Release();
        }
    }

    private CatalogParseOutcome Parse(string document, CatalogSource source)
    {
        try
        {
            var result = _parser.Parse(document, source);
            return new CatalogParseOutcome(
                result.Succeeded,
                result.Value,
                result.Error,
                Exception: null
            );
        }
        catch (Exception ex)
        {
            return new CatalogParseOutcome(false, default, ex.Message, ex);
        }
    }

    private async Task WaitForWarmCallerAsync(
        WarmFlight flight,
        CancellationToken cancellationToken
    )
    {
        var released = 0;
        void ReleaseOnce()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                ReleaseWarmWaiter(flight);
        }

        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(ReleaseOnce)
            : default;
        try
        {
            await WaitWithCancellationAsync(flight.Task, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseOnce();
        }
    }

    private async Task<CatalogRefreshResult<TSnapshot>> WaitForRefreshCallerAsync(
        RefreshFlight flight,
        CancellationToken cancellationToken
    )
    {
        var released = 0;
        void ReleaseOnce()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                ReleaseRefreshWaiter(flight);
        }

        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(ReleaseOnce)
            : default;
        try
        {
            return await WaitWithCancellationAsync(flight.Task, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseOnce();
        }
    }

    private void ReleaseWarmWaiter(WarmFlight flight)
    {
        var cancel = false;
        var disposeCancellation = false;
        lock (_sync)
        {
            flight.WaiterCount--;
            if (flight.WaiterCount == 0 && !flight.Task.IsCompleted)
            {
                if (ReferenceEquals(_warmFlight, flight))
                    _warmFlight = null;
                cancel = AbandonFlight(flight, disposedByOwner: false);
            }
            disposeCancellation = TryClaimCancellationDisposal(flight);
        }

        if (cancel)
            CancelFlight(flight);
        else if (disposeCancellation)
            flight.Cancellation.Dispose();
    }

    private void ReleaseRefreshWaiter(RefreshFlight flight)
    {
        var cancel = false;
        var disposeCancellation = false;
        lock (_sync)
        {
            flight.WaiterCount--;
            if (flight.WaiterCount == 0 && !flight.Task.IsCompleted)
            {
                if (ReferenceEquals(_refreshFlight, flight))
                    _refreshFlight = null;
                cancel = AbandonFlight(flight, disposedByOwner: false);
            }
            disposeCancellation = TryClaimCancellationDisposal(flight);
        }

        if (cancel)
            CancelFlight(flight);
        else if (disposeCancellation)
            flight.Cancellation.Dispose();
    }

    private bool AbandonFlight(CatalogFlight flight, bool disposedByOwner)
    {
        flight.Abandoned = true;
        flight.DisposedByOwner |= disposedByOwner;
        if (flight.CancelStarted)
            return false;

        flight.CancelStarted = true;
        return true;
    }

    private void CancelFlight(CatalogFlight flight)
    {
        try
        {
            flight.Cancellation.Cancel();
        }
        catch
        {
            // A source's cancellation callback cannot be allowed to escape through a caller's
            // CancellationTokenSource.Cancel() or catalog disposal.
        }
        finally
        {
            var disposeCancellation = false;
            lock (_sync)
            {
                flight.CancelFinished = true;
                disposeCancellation = TryClaimCancellationDisposal(flight);
            }
            if (disposeCancellation)
                flight.Cancellation.Dispose();
        }
    }

    private static bool TryClaimCancellationDisposal(CatalogFlight flight)
    {
        if (
            flight.CancellationDisposed
            || !flight.CompletionFinished
            || (flight.CancelStarted && !flight.CancelFinished)
        )
        {
            return false;
        }

        flight.CancellationDisposed = true;
        return true;
    }

    private bool WasDisposed(CatalogFlight flight)
    {
        lock (_sync)
            return _disposed || flight.DisposedByOwner;
    }

    private void MarkBackgroundRefreshDequeued(int ticket, int generation)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation)
                return;

            if (_scheduledBackgroundRefreshTicket != ticket)
                return;

            _scheduledBackgroundRefreshTicket = null;
            if (!_snapshot.HasValue && _refreshFlight == null)
                _warmCompleted = false;
        }
    }

    private static async Task WaitWithCancellationAsync(
        Task task,
        CancellationToken cancellationToken
    )
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await task.ConfigureAwait(false);
            return;
        }

        var cancellationSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var registration = cancellationToken.Register(() =>
            cancellationSignal.TrySetResult(true)
        );
        if (task != await Task.WhenAny(task, cancellationSignal.Task).ConfigureAwait(false))
            throw new OperationCanceledException(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await task.ConfigureAwait(false);
    }

    private static async Task<TResult> WaitWithCancellationAsync<TResult>(
        Task<TResult> task,
        CancellationToken cancellationToken
    )
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await task.ConfigureAwait(false);
        }

        var cancellationSignal = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var registration = cancellationToken.Register(() =>
            cancellationSignal.TrySetResult(true)
        );
        if (task != await Task.WhenAny(task, cancellationSignal.Task).ConfigureAwait(false))
            throw new OperationCanceledException(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await task.ConfigureAwait(false);
    }

    private bool ObserveIfCurrent(
        CatalogFlight flight,
        int generation,
        Action<IRemoteEmbeddedCatalogObserver<TSnapshot>> notify
    )
    {
        lock (_observerSync)
        {
            lock (_sync)
            {
                if (_disposed || flight.Abandoned || generation != _generation)
                    return false;
            }

            Observe(notify);
            return true;
        }
    }

    private bool ObserveIfCurrent(
        CatalogFlight flight,
        int generation,
        int publicationVersion,
        Action<IRemoteEmbeddedCatalogObserver<TSnapshot>> notify
    )
    {
        lock (_observerSync)
        {
            lock (_sync)
            {
                if (
                    _disposed
                    || flight.Abandoned
                    || generation != _generation
                    || publicationVersion != _publicationVersion
                )
                    return false;
            }

            Observe(notify);
            return true;
        }
    }

    private bool ObserveIfCurrent(
        int generation,
        Action<IRemoteEmbeddedCatalogObserver<TSnapshot>> notify
    )
    {
        lock (_observerSync)
        {
            lock (_sync)
            {
                if (_disposed || generation != _generation)
                    return false;
            }

            Observe(notify);
            return true;
        }
    }

    private bool ObserveIfCurrent(
        int generation,
        int publicationVersion,
        Action<IRemoteEmbeddedCatalogObserver<TSnapshot>> notify
    )
    {
        lock (_observerSync)
        {
            lock (_sync)
            {
                if (
                    _disposed
                    || generation != _generation
                    || publicationVersion != _publicationVersion
                )
                    return false;
            }

            Observe(notify);
            return true;
        }
    }

    private bool ObserveRefreshIfCurrent(
        CatalogFlight flight,
        int generation,
        CatalogRefreshTrigger trigger,
        CatalogRefreshResult<TSnapshot> result
    ) =>
        ObserveIfCurrent(
            flight,
            generation,
            observer => observer.OnRefreshCompleted(trigger, result)
        );

    private void Observe(Action<IRemoteEmbeddedCatalogObserver<TSnapshot>> notify)
    {
        try
        {
            notify(_observer);
        }
        catch
        {
            // Observability is deliberately fail-open: feature logging cannot change catalog state.
        }
    }

    private readonly record struct CatalogParseOutcome(
        bool Succeeded,
        TSnapshot? Value,
        string? Error,
        Exception? Exception
    );

    private abstract class CatalogFlight
    {
        internal CancellationTokenSource Cancellation { get; } = new();
        internal int WaiterCount { get; set; }
        internal bool Abandoned { get; set; }
        internal bool DisposedByOwner { get; set; }
        internal bool CancelStarted { get; set; }
        internal bool CancelFinished { get; set; }
        internal bool CompletionFinished { get; set; }
        internal bool CancellationDisposed { get; set; }
    }

    private sealed class WarmFlight : CatalogFlight
    {
        internal TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Task => Completion.Task;
    }

    private sealed class RefreshFlight : CatalogFlight
    {
        internal TaskCompletionSource<CatalogRefreshResult<TSnapshot>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<CatalogRefreshResult<TSnapshot>> Task => Completion.Task;
    }
}
