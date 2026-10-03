#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.BundlePipeline;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using BazaarPlusPlus.ModApi;
using UnityEngine;

namespace BazaarPlusPlus.Game.Upload;

internal sealed class BackgroundUploadPump : MonoBehaviour
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500);

    private IBppServices? _services;
    private BundleUploadFeed.Session? _session;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private StartupUploadAttemptRunner? _startupRunner;
    private UploadFeedLogState? _logState;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _uploadArmSubscription;
    private IDisposable? _feedArmSubscription;
    private int _armRequested;

    private void Awake() { }

    public void Initialize(IBppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logState = new UploadFeedLogState();

        var cadence = new UploadPumpCadence(
            ModApiUploadDefaults.StartupDelaySeconds,
            ModApiUploadDefaults.IntervalSeconds
        );

        var session = UploadPumpBootstrap.ActivateIfAllowed(_services, cadence);
        if (session == null)
            return;

        _session = session;
        _shutdown = new CancellationTokenSource();
        _startupGate = new StartupUploadAttemptGate(
            Time.unscaledTime + cadence.StartupDelaySeconds,
            cadence.RetryIntervalSeconds
        );
        _startupRunner = new StartupUploadAttemptRunner(_logState);
        _runLifecycleSubscription = _services.EventBus.Subscribe<RunLifecycleChanged>(
            OnRunLifecycleChanged
        );
        _uploadArmSubscription = _services.EventBus.Subscribe<UploadArmRequested>(
            OnUploadArmRequested
        );
        _feedArmSubscription = session.SubscribeArmSignals(ArmImmediate);
    }

    private void Update()
    {
        if (
            _session == null
            || _shutdown == null
            || _startupGate == null
            || _startupRunner == null
            || _services == null
        )
            return;

        if (!_session.IsEnabled)
            return;

        if (Interlocked.Exchange(ref _armRequested, 0) == 1)
            _startupGate.ArmImmediateAttempt(Time.unscaledTime);

        _startupRunner.Tick(
            _startupGate,
            Time.unscaledTime,
            _services.RunContext.IsInGameRun,
            _session.RunAttemptAsync,
            _shutdown.Token
        );
    }

    private void OnDestroy()
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
        _logState = null;
        _services = null;
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
        if (_services == null || request == null)
            return;

        ArmImmediate();
    }
}
