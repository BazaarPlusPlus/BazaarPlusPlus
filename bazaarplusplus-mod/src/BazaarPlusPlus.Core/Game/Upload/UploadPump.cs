#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.Upload;

// Call Tick on the host's main thread with a monotonic clock in seconds.
public sealed class UploadPump : IDisposable
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IBppServices _services;
    private BundleUploadFeed.Session? _session;

    // Converted once: a method group passed to Tick would allocate a delegate every frame.
    private Func<CancellationToken, Task<UploadAttemptResult>>? _runAttempt;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private StartupUploadAttemptRunner? _startupRunner;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _uploadArmSubscription;
    private IDisposable? _feedArmSubscription;
    private int _armRequested;

    private UploadPump(IBppServices services) => _services = services;

    public static UploadPump Start(
        IBppServices services,
        string? platformKey,
        float nowSeconds,
        SqliteOpenOptions databaseOptions
    )
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));
        var pump = new UploadPump(services);
        var cadence = new UploadPumpCadence(
            ModApiUploadDefaults.StartupDelaySeconds,
            ModApiUploadDefaults.IntervalSeconds
        );
        var session = UploadPumpBootstrap.ActivateIfAllowed(
            services,
            cadence,
            platformKey,
            databaseOptions
        );
        if (session == null)
            return pump;

        pump._session = session;
        pump._runAttempt = session.RunAttemptAsync;
        pump._shutdown = new CancellationTokenSource();
        pump._startupGate = new StartupUploadAttemptGate(
            nowSeconds + cadence.StartupDelaySeconds,
            cadence.RetryIntervalSeconds
        );
        pump._startupRunner = new StartupUploadAttemptRunner(new UploadFeedLogState());
        // The bus invokes subscribers in order on the publisher's thread. Subscribe the feed first so
        // its backoff clears before the pump can arm a main-thread attempt.
        pump._feedArmSubscription = session.SubscribeArmSignals();
        pump._runLifecycleSubscription = services.EventBus.Subscribe<RunLifecycleChanged>(
            pump.OnRunLifecycleChanged
        );
        pump._uploadArmSubscription = services.EventBus.Subscribe<UploadArmRequested>(
            pump.OnUploadArmRequested
        );
        return pump;
    }

    public void Tick(float nowSeconds)
    {
        if (
            _runAttempt == null
            || _shutdown == null
            || _startupGate == null
            || _startupRunner == null
        )
            return;

        if (Interlocked.Exchange(ref _armRequested, 0) == 1)
            _startupGate.ArmImmediateAttempt(nowSeconds);

        _startupRunner.Tick(
            _startupGate,
            nowSeconds,
            _services.RunContext.IsInGameRun,
            _runAttempt,
            _shutdown.Token
        );
    }

    public void Dispose()
    {
        // Unsubscribe before canceling so the pump cannot be re-armed during teardown.
        // Drain the attempt before disposing its session resources.
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;
        _uploadArmSubscription?.Dispose();
        _uploadArmSubscription = null;
        _feedArmSubscription?.Dispose();
        _feedArmSubscription = null;

        if (_shutdown != null)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            _shutdown = null;
        }

        var session = _session;
        _session = null;
        _runAttempt = null;
        Action? disposeSession = session == null ? null : session.Dispose;
        if (_startupRunner != null)
        {
            if (!_startupRunner.TryDrainPendingTaskOnShutdown(ShutdownDrainTimeout, disposeSession))
            {
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.Upload,
                        "upload.shutdown_drain.degraded",
                        storm: ["reason_code"]
                    ),
                    ("timeout_ms", (long)ShutdownDrainTimeout.TotalMilliseconds),
                    ("reason_code", UploadLogReasonCode.ShutdownDrainTimeout)
                );
            }
        }
        else
            disposeSession?.Invoke();

        _startupGate = null;
        _startupRunner = null;
    }

    private void ArmImmediate()
    {
        Interlocked.Exchange(ref _armRequested, 1);
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        if (change.IsInGameRun)
            return;

        ArmImmediate();
    }

    private void OnUploadArmRequested(UploadArmRequested request)
    {
        if (request == null)
            return;

        ArmImmediate();
    }
}
