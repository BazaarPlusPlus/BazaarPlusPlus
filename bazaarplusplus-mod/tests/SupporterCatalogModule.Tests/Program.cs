using BazaarPlusPlus.Game.Supporters;
using BazaarPlusPlus.Infrastructure.RemoteEmbeddedCatalog;

TestFixedModeNeverCreatesCatalog();
TestWarmPublishesOnceAndKeepsSessionSnapshot();
TestMissingSnapshotRetriesNoSoonerThanFiveMinutes();
TestStopDisposesBeforeResetAndRejectsLatePublish();

Console.WriteLine("Supporter catalog module checks passed.");

static void TestFixedModeNeverCreatesCatalog()
{
    var factoryCalls = 0;
    var module = new SupporterCatalogModule(
        () => true,
        _ =>
        {
            factoryCalls++;
            return new FakeCatalog();
        },
        () => DateTime.UnixEpoch
    );

    module.Start();
    module.EnsureWarmScheduled();
    module.EnsureWarmScheduled();
    module.Stop();

    Equal(0, factoryCalls, "Fixed-list mode must bypass catalog construction and warming.");
}

static void TestWarmPublishesOnceAndKeepsSessionSnapshot()
{
    BPPSupporterCatalog.Reset();
    var catalog = new FakeCatalog(publishOnWarm: true);
    var module = new SupporterCatalogModule(
        () => false,
        observer => catalog.Attach(observer),
        () => DateTime.UnixEpoch
    );

    module.Start();
    module.EnsureWarmScheduled();
    module.EnsureWarmScheduled();

    Equal(1, catalog.WarmCalls, "A published session snapshot should prevent duplicate warms.");
    Equal(
        "Remote Supporter",
        BPPSupporterCatalog.GetCurrentEntries()[0].Name,
        "The facade should synchronously project the catalog snapshot."
    );
    module.Stop();
}

static void TestMissingSnapshotRetriesNoSoonerThanFiveMinutes()
{
    var now = DateTime.UnixEpoch;
    var catalog = new FakeCatalog();
    var module = new SupporterCatalogModule(
        () => false,
        observer => catalog.Attach(observer),
        () => now
    );

    module.Start();
    module.EnsureWarmScheduled();
    now = now.AddMinutes(4).AddSeconds(59);
    module.EnsureWarmScheduled();
    Equal(1, catalog.WarmCalls, "A missing snapshot should be latched for five minutes.");

    now = now.AddSeconds(1);
    module.EnsureWarmScheduled();
    Equal(2, catalog.WarmCalls, "The module should retry when the five-minute latch expires.");
    module.Stop();
}

static void TestStopDisposesBeforeResetAndRejectsLatePublish()
{
    BPPSupporterCatalog.Reset();
    var catalog = new FakeCatalog(publishOnWarm: true);
    var module = new SupporterCatalogModule(
        () => false,
        observer => catalog.Attach(observer),
        () => DateTime.UnixEpoch
    );

    module.Start();
    module.Stop();
    catalog.Publish("Late Supporter");

    True(catalog.Disposed, "Stopping the feature must dispose its catalog.");
    False(
        BPPSupporterCatalog.GetCurrentEntries().Any(entry => entry.Name == "Late Supporter"),
        "A catalog callback arriving after Stop must not republish into the facade."
    );
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message} Expected={expected}; Actual={actual}");
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void False(bool condition, string message) => True(!condition, message);

internal sealed class FakeCatalog : IRemoteEmbeddedCatalog<IReadOnlyList<BPPSupporterEntry>>
{
    private IRemoteEmbeddedCatalogObserver<IReadOnlyList<BPPSupporterEntry>>? _observer;
    private readonly bool _publishOnWarm;
    private CatalogSnapshot<IReadOnlyList<BPPSupporterEntry>>? _snapshot;

    internal FakeCatalog(bool publishOnWarm = false)
    {
        _publishOnWarm = publishOnWarm;
    }

    internal int WarmCalls { get; private set; }
    internal bool Disposed { get; private set; }

    internal FakeCatalog Attach(
        IRemoteEmbeddedCatalogObserver<IReadOnlyList<BPPSupporterEntry>> observer
    )
    {
        _observer = observer;
        return this;
    }

    public bool TryGet(out CatalogSnapshot<IReadOnlyList<BPPSupporterEntry>> snapshot)
    {
        snapshot = _snapshot.GetValueOrDefault();
        return _snapshot.HasValue;
    }

    public ValueTask WarmAsync(CancellationToken cancellationToken = default)
    {
        WarmCalls++;
        _observer?.OnWarmStarted();
        if (_publishOnWarm)
        {
            Publish("Remote Supporter");
            _observer?.OnInitialLoad(
                CatalogInitialLoadResult<IReadOnlyList<BPPSupporterEntry>>.Published(
                    _snapshot!.Value
                )
            );
        }
        else
        {
            _observer?.OnInitialLoad(
                CatalogInitialLoadResult<IReadOnlyList<BPPSupporterEntry>>.Unavailable(
                    new CatalogIssue(CatalogIssueKind.RemoteEmpty)
                )
            );
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<CatalogRefreshResult<IReadOnlyList<BPPSupporterEntry>>> RefreshAsync(
        CancellationToken cancellationToken = default
    ) =>
        ValueTask.FromResult(
            CatalogRefreshResult<IReadOnlyList<BPPSupporterEntry>>.Failure(
                new CatalogIssue(CatalogIssueKind.RemoteEmpty)
            )
        );

    public void Dispose()
    {
        Disposed = true;
    }

    internal void Publish(string name)
    {
        _snapshot = new CatalogSnapshot<IReadOnlyList<BPPSupporterEntry>>(
            new[]
            {
                new BPPSupporterEntry { Name = name, Tier = 4 },
            },
            CatalogSource.Remote,
            DateTime.UnixEpoch,
            IsStale: false,
            Issue: null
        );
        _observer?.OnRefreshCompleted(
            CatalogRefreshTrigger.Background,
            CatalogRefreshResult<IReadOnlyList<BPPSupporterEntry>>.Published(
                _snapshot.Value,
                degraded: false
            )
        );
    }
}
