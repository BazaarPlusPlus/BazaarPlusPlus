#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi;
using BazaarPlusPlus.Storage.Sqlite;

namespace BazaarPlusPlus.Game.Upload;

/// <summary>
/// The one upload pump of a host: it drives the <see cref="BundleUploadFeed"/> session on the
/// startup delay and retry interval, arms an immediate attempt when a run ends or a feature asks,
/// and never starts one while a run is live. The host calls <see cref="Tick"/> from its main
/// thread with a monotonic clock in seconds, and <see cref="Dispose"/> once at teardown.
/// </summary>
public sealed class UploadPump : IDisposable
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IBppServices _services;
    private BundleUploadFeed.Session? _session;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private StartupUploadAttemptRunner? _startupRunner;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _uploadArmSubscription;
    private IDisposable? _feedArmSubscription;
    private int _armRequested;

    private UploadPump(IBppServices services) => _services = services;

    /// <summary>
    /// Activates the feed and starts the startup delay at <paramref name="nowSeconds"/>. On a PTR
    /// build the pump stays inert: every tick does nothing.
    /// </summary>
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
        pump._shutdown = new CancellationTokenSource();
        pump._startupGate = new StartupUploadAttemptGate(
            nowSeconds + cadence.StartupDelaySeconds,
            cadence.RetryIntervalSeconds
        );
        pump._startupRunner = new StartupUploadAttemptRunner(new UploadFeedLogState());
        pump._runLifecycleSubscription = services.EventBus.Subscribe<RunLifecycleChanged>(
            pump.OnRunLifecycleChanged
        );
        pump._uploadArmSubscription = services.EventBus.Subscribe<UploadArmRequested>(
            pump.OnUploadArmRequested
        );
        pump._feedArmSubscription = session.SubscribeArmSignals(pump.ArmImmediate);
        return pump;
    }

    public void Tick(float nowSeconds)
    {
        if (_session == null || _shutdown == null || _startupGate == null || _startupRunner == null)
            return;

        if (Interlocked.Exchange(ref _armRequested, 0) == 1)
            _startupGate.ArmImmediateAttempt(nowSeconds);

        _startupRunner.Tick(
            _startupGate,
            nowSeconds,
            _services.RunContext.IsInGameRun,
            _session.RunAttemptAsync,
            _shutdown.Token
        );
    }

    public void Dispose()
    {
        // Two-point dispose contract (must not collapse into a single Dispose):
        // 1) Unsubscribe arm signals first so a half-torn pump cannot be re-armed.
        // 2) Cancel the in-flight attempt, drain, then dispose session attempt resources.
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
