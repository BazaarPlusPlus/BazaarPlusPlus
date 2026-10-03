#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

namespace BazaarPlusPlus.Game.Supporters;

internal sealed class SupporterCatalogModule : IBppFeature
{
    private static readonly TimeSpan MissingSnapshotRetryDelay = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private readonly Func<bool> _isFixedListEnabled;
    private readonly Func<
        IRemoteEmbeddedCatalogObserver<IReadOnlyList<BPPSupporterEntry>>,
        IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>
    > _catalogFactory;
    private readonly Func<DateTime> _utcNow;
    private readonly string _cachePath;
    private IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>? _catalog;
    private Task? _warmTask;
    private DateTime _retryAtUtc = DateTime.MinValue;
    private bool _started;
    private bool _stopped;

    internal SupporterCatalogModule(Func<bool> isFixedListEnabled, string dataRootPath)
        : this(
            isFixedListEnabled,
            observer => SupporterCatalogFactory.Create(dataRootPath, observer),
            () => DateTime.UtcNow,
            SupporterCatalogFactory.BuildCacheFilePath(dataRootPath)
        ) { }

    internal SupporterCatalogModule(
        Func<bool> isFixedListEnabled,
        Func<
            IRemoteEmbeddedCatalogObserver<IReadOnlyList<BPPSupporterEntry>>,
            IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>
        > catalogFactory,
        Func<DateTime> utcNow,
        string cachePath = "supporter-list.json"
    )
    {
        _isFixedListEnabled =
            isFixedListEnabled ?? throw new ArgumentNullException(nameof(isFixedListEnabled));
        _catalogFactory = catalogFactory ?? throw new ArgumentNullException(nameof(catalogFactory));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _cachePath = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
                return;
            _started = true;
            _stopped = false;
        }

        BPPSupporterCatalog.Attach(EnsureWarmScheduled);
        if (!_isFixedListEnabled())
            EnsureWarmScheduled();
    }

    public void Stop()
    {
        IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>? catalog;
        lock (_sync)
        {
            if (_stopped)
                return;
            _stopped = true;
            catalog = _catalog;
            _catalog = null;
            _warmTask = null;
        }

        catalog?.Dispose();
        BPPSupporterCatalog.DetachAndResetProjection();
    }

    internal void EnsureWarmScheduled()
    {
        if (_isFixedListEnabled())
            return;

        lock (_sync)
        {
            if (!_started || _stopped)
                return;
            if (_catalog?.TryGet(out _) == true)
                return;
            if (_warmTask is { IsCompleted: false } || _utcNow() < _retryAtUtc)
                return;

            _catalog ??= _catalogFactory(new Observer(Publish, _cachePath));
            _warmTask = WarmAsync(_catalog);
        }
    }

    private async Task WarmAsync(IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>> catalog)
    {
        try
        {
            await catalog.WarmAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (!_stopped && ReferenceEquals(_catalog, catalog))
                {
                    _warmTask = null;
                    _retryAtUtc = catalog.TryGet(out _)
                        ? DateTime.MaxValue
                        : _utcNow().Add(MissingSnapshotRetryDelay);
                }
            }
        }
    }

    private void Publish(IReadOnlyList<BPPSupporterEntry> entries)
    {
        lock (_sync)
        {
            if (_stopped)
                return;
            BPPSupporterCatalog.Publish(entries);
        }
    }

    private sealed class Observer : IRemoteEmbeddedCatalogObserver<IReadOnlyList<BPPSupporterEntry>>
    {
        private readonly Action<IReadOnlyList<BPPSupporterEntry>> _publish;
        private readonly string _cachePath;
        private readonly OperationalHealthTracker<
            SupporterCatalogOperation,
            SupporterCatalogFailure
        > _catalogHealth = new();
        private readonly OperationalHealthTracker<
            SupporterCacheOperation,
            SupporterLogReasonCode
        > _cacheWriteHealth = new();

        internal Observer(Action<IReadOnlyList<BPPSupporterEntry>> publish, string cachePath)
        {
            _publish = publish;
            _cachePath = cachePath;
        }

        public void OnWarmStarted() { }

        public void OnInitialLoad(CatalogInitialLoadResult<IReadOnlyList<BPPSupporterEntry>> result)
        {
            if (result.Snapshot is { } snapshot)
            {
                _publish(snapshot.Value);
                if (
                    result.Issue is { } issue
                    && issue.Kind
                        is CatalogIssueKind.CacheReadFailed
                            or CatalogIssueKind.CacheInvalid
                )
                {
                    ReportCatalogFailure(
                        SupporterCatalogSource.DiskCache,
                        MapReason(issue.Kind),
                        issue.Exception
                    );
                }
                else
                {
                    ReportCatalogSuccess(MapSource(snapshot.Source), snapshot.Value.Count);
                }
                return;
            }

            var unavailable = result.Issue ?? new CatalogIssue(CatalogIssueKind.Unexpected);
            ReportCatalogFailure(
                MapIssueSource(unavailable.Kind),
                MapReason(unavailable.Kind),
                unavailable.Exception
            );
        }

        public void OnRefreshQueued(CatalogIssue reason) { }

        public void OnRefreshCompleted(
            CatalogRefreshTrigger trigger,
            CatalogRefreshResult<IReadOnlyList<BPPSupporterEntry>> result
        )
        {
            if (result.Snapshot is { } snapshot)
            {
                _publish(snapshot.Value);
                if (snapshot.Issue is { Kind: CatalogIssueKind.CacheWriteFailed } cacheIssue)
                    ReportCacheWriteFailure(cacheIssue.Exception);
                else
                    ReportCacheWriteSuccess();
                ReportCatalogSuccess(SupporterCatalogSource.Remote, snapshot.Value.Count);
                return;
            }

            var issue = result.Issue ?? new CatalogIssue(CatalogIssueKind.Unexpected);
            ReportCatalogFailure(
                MapIssueSource(issue.Kind),
                MapReason(issue.Kind),
                issue.Exception
            );
        }

        private void ReportCatalogFailure(
            SupporterCatalogSource source,
            SupporterLogReasonCode reason,
            Exception? exception
        )
        {
            if (
                !_catalogHealth.ObserveFailure(
                    SupporterCatalogOperation.Load,
                    new SupporterCatalogFailure(source, reason)
                )
            )
                return;

            var fields = new BppLogField[]
            {
                ("source", source),
                ("reason_code", reason),
                ("cache_path", _cachePath),
            };
            if (exception == null)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Supporters,
                        "supporters.catalog.degraded",
                        storm: ["source", "reason_code"]
                    ),
                    fields
                );
            else
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Supporters,
                        "supporters.catalog.degraded",
                        storm: ["source", "reason_code"]
                    ),
                    exception,
                    fields
                );
        }

        private void ReportCatalogSuccess(SupporterCatalogSource source, int entryCount)
        {
            if (!_catalogHealth.ObserveSuccess(SupporterCatalogOperation.Load, out var failure))
            {
                BppLog.DebugEvent(
                    new BppLogEvent(BppLogFeatureScope.Supporters, "supporters.catalog.loaded"),
                    () => [("source", source), ("entry_count", entryCount)]
                );
                return;
            }

            BppLog.RecoverStorm(
                new BppLogEvent(
                    BppLogFeatureScope.Supporters,
                    "supporters.catalog.degraded",
                    storm: ["source", "reason_code"]
                ),
                ("source", failure.Source),
                ("reason_code", failure.Reason)
            );
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Supporters, "supporters.catalog.recovered"),
                ("source", source),
                ("entry_count", entryCount)
            );
        }

        private void ReportCacheWriteFailure(Exception? exception)
        {
            if (
                !_cacheWriteHealth.ObserveFailure(
                    SupporterCacheOperation.Write,
                    SupporterLogReasonCode.WriteException
                )
            )
                return;

            var fields = new BppLogField[]
            {
                ("path", _cachePath),
                ("reason_code", SupporterLogReasonCode.WriteException),
            };
            if (exception == null)
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Supporters,
                        "supporters.cache.write_degraded",
                        storm: ["reason_code"]
                    ),
                    fields
                );
            else
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Supporters,
                        "supporters.cache.write_degraded",
                        storm: ["reason_code"]
                    ),
                    exception,
                    fields
                );
        }

        private void ReportCacheWriteSuccess()
        {
            if (!_cacheWriteHealth.ObserveSuccess(SupporterCacheOperation.Write, out var reason))
                return;

            BppLog.RecoverStorm(
                new BppLogEvent(
                    BppLogFeatureScope.Supporters,
                    "supporters.cache.write_degraded",
                    storm: ["reason_code"]
                ),
                ("reason_code", reason)
            );
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Supporters, "supporters.cache.write_recovered"),
                ("path", _cachePath)
            );
        }

        private static SupporterCatalogSource MapSource(CatalogSource source) =>
            source switch
            {
                CatalogSource.Cache => SupporterCatalogSource.DiskCache,
                CatalogSource.Embedded => SupporterCatalogSource.BundledFallback,
                CatalogSource.Remote => SupporterCatalogSource.Remote,
                _ => SupporterCatalogSource.BundledFallback,
            };

        private static SupporterCatalogSource MapIssueSource(CatalogIssueKind issue) =>
            issue switch
            {
                CatalogIssueKind.CacheMissing
                or CatalogIssueKind.CacheStale
                or CatalogIssueKind.CacheReadFailed
                or CatalogIssueKind.CacheInvalid
                or CatalogIssueKind.CacheWriteFailed => SupporterCatalogSource.DiskCache,
                CatalogIssueKind.EmbeddedMissing
                or CatalogIssueKind.EmbeddedReadFailed
                or CatalogIssueKind.EmbeddedInvalid => SupporterCatalogSource.BundledFallback,
                _ => SupporterCatalogSource.Remote,
            };

        private static SupporterLogReasonCode MapReason(CatalogIssueKind issue) =>
            issue switch
            {
                CatalogIssueKind.CacheReadFailed or CatalogIssueKind.EmbeddedReadFailed =>
                    SupporterLogReasonCode.ReadException,
                CatalogIssueKind.CacheWriteFailed => SupporterLogReasonCode.WriteException,
                CatalogIssueKind.CacheInvalid
                or CatalogIssueKind.EmbeddedInvalid
                or CatalogIssueKind.EmbeddedMissing
                or CatalogIssueKind.RemoteEmpty => SupporterLogReasonCode.EmptyPayload,
                _ => SupporterLogReasonCode.RefreshException,
            };
    }
}
