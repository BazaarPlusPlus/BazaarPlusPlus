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
    private static readonly Dictionary<UploadFeedKind, BackgroundUploadPump> CurrentByFeed = new();

    private IBppServices? _services;
    private IUploadFeed? _feed;
    private UploadFeedActivation? _activation;
    private CancellationTokenSource? _shutdown;
    private StartupUploadAttemptGate? _startupGate;
    private StartupUploadAttemptRunner? _startupRunner;
    private UploadFeedLogState? _logState;
    private IDisposable? _runLifecycleSubscription;
    private IDisposable? _extraArmHookSubscription;

    private void Awake() { }

    public void Initialize(IBppServices services, IUploadFeed feed)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));

        var feedKind = _feed.Kind;
        _logState = new UploadFeedLogState(feedKind);
        if (services.GameBuild.Channel == GameBuildChannel.Ptr)
        {
            // Session gate: no upload feed arms on the PTR build. The durable defense
            // is the build_channel row filter in the upload stores — it keeps
            // PTR-recorded rows out of uploads even after switching back to online.
            BppLog.DebugEvent(
                UploadLogEvents.FeedSkipped,
                () =>
                    [
                        UploadLogEvents.FeedSkippedFeed.Bind(feedKind),
                        UploadLogEvents.FeedSkippedReasonCode.Bind(UploadLogReasonCode.PtrBuild),
                    ]
            );
            return;
        }
        var activation = _feed.Activate(_services, _logState);
        if (activation == null)
            return;

        var startupDelaySeconds = Math.Max(5, ModApiUploadDefaults.StartupDelaySeconds);
        var retryIntervalSeconds = Math.Max(1, ModApiUploadDefaults.IntervalSeconds);

        _activation = activation;
        _shutdown = new CancellationTokenSource();
        _startupGate = new StartupUploadAttemptGate(
            Time.unscaledTime + startupDelaySeconds,
            retryIntervalSeconds
        );
        _startupRunner = new StartupUploadAttemptRunner(feedKind, _logState);
        _runLifecycleSubscription = _services.EventBus.Subscribe<RunLifecycleChanged>(
            OnRunLifecycleChanged
        );
        _extraArmHookSubscription = activation.ExtraArmHook?.Subscribe(_services, ArmImmediate);
        CurrentByFeed[feedKind] = this;
    }

    private void Update()
    {
        if (
            _activation == null
            || _shutdown == null
            || _startupGate == null
            || _startupRunner == null
            || _services == null
        )
            return;

        if (!_activation.IsEnabled())
            return;

        _startupRunner.Tick(
            _startupGate,
            Time.unscaledTime,
            _services.RunContext.IsInGameRun,
            _activation.UploadInBackgroundAsync,
            _shutdown.Token
        );
    }

    private void OnDestroy()
    {
        _runLifecycleSubscription?.Dispose();
        _runLifecycleSubscription = null;
        _extraArmHookSubscription?.Dispose();
        _extraArmHookSubscription = null;

        if (_shutdown != null)
        {
            _shutdown.Cancel();
            _shutdown.Dispose();
            _shutdown = null;
        }

        var feedKind = _feed?.Kind;
        var activation = _activation;
        _activation = null;
        Action? disposeActivation =
            activation?.Disposable == null ? null : activation.Disposable.Dispose;
        if (_startupRunner != null)
        {
            if (
                !_startupRunner.TryDrainPendingTaskOnShutdown(
                    ShutdownDrainTimeout,
                    disposeActivation
                )
            )
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
            disposeActivation?.Invoke();

        if (feedKind.HasValue && CurrentByFeed.TryGetValue(feedKind.Value, out var current))
        {
            if (ReferenceEquals(current, this))
                CurrentByFeed.Remove(feedKind.Value);
        }

        _startupGate = null;
        _startupRunner = null;
        _logState = null;
        _services = null;
        _feed = null;
    }

    public static void ArmImmediate(UploadFeedKind feed)
    {
        if (CurrentByFeed.TryGetValue(feed, out var current))
            current.ArmImmediate();
    }

    private void ArmImmediate()
    {
        _startupGate?.ArmImmediateAttempt(Time.unscaledTime);
    }

    private void OnRunLifecycleChanged(RunLifecycleChanged change)
    {
        if (change.IsInGameRun)
            return;

        ArmImmediate();
    }
}
