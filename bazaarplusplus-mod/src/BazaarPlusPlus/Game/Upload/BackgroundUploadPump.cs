#nullable enable
using BazaarPlusPlus.Core.Events;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.ModApi;
using UnityEngine;

namespace BazaarPlusPlus.Game.Upload;

internal sealed class BackgroundUploadPump : MonoBehaviour
{
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromMilliseconds(500);

    private IBppServices? _services;
    private IUploadFeed? _feed;
    private IUploadFeedSession? _session;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private StartupUploadAttemptRunner? _startupRunner;
    private UploadFeedLogState? _logState;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _uploadArmSubscription;
    private IDisposable? _feedArmSubscription;
    private int _armRequested;

    private void Awake() { }

    public void Initialize(IBppServices services, IUploadFeed feed)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));

        var feedKind = _feed.Kind;
        _logState = new UploadFeedLogState(feedKind);

        var startupDelaySeconds = Math.Max(5, ModApiUploadDefaults.StartupDelaySeconds);
        var retryIntervalSeconds = Math.Max(1, ModApiUploadDefaults.IntervalSeconds);
        var cadence = new UploadPumpCadence(startupDelaySeconds, retryIntervalSeconds);

        var session = UploadPumpBootstrap.ActivateIfAllowed(_services, _feed, _logState, cadence);
        if (session == null)
            return;

        _session = session;
        _shutdown = new CancellationTokenSource();
        _startupGate = new StartupUploadAttemptGate(
            Time.unscaledTime + startupDelaySeconds,
            retryIntervalSeconds
        );
        _startupRunner = new StartupUploadAttemptRunner(feedKind, _logState);
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

        var feedKind = _feed?.Kind;
        var session = _session;
        _session = null;
        Action? disposeSession = session == null ? null : session.Dispose;
        if (_startupRunner != null)
        {
            if (!_startupRunner.TryDrainPendingTaskOnShutdown(ShutdownDrainTimeout, disposeSession))
            {
                BppLog.WarnEvent(
                    UploadLogEvents.ShutdownDrainDegraded,
                    UploadLogEvents.ShutdownDrainDegradedFeed.Bind(feedKind),
                    UploadLogEvents.ShutdownDrainDegradedTimeoutMs.Bind(
                        (long)ShutdownDrainTimeout.TotalMilliseconds
                    ),
                    UploadLogEvents.ShutdownDrainDegradedReasonCode.Bind(
                        UploadLogReasonCode.ShutdownDrainTimeout
                    )
                );
            }
        }
        else
            disposeSession?.Invoke();

        _startupGate = null;
        _startupRunner = null;
        _logState = null;
        _services = null;
        _feed = null;
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
        if (_feed == null || request == null)
            return;

        ArmImmediate();
    }
}
