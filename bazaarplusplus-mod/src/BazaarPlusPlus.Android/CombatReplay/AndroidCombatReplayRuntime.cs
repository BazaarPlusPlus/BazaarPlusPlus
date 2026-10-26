using BazaarPlusPlus.Android.GameInterop;
using BazaarPlusPlus.Android.Host;
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CombatReplay;
using BazaarPlusPlus.Game.RunLifecycle;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.CombatReplay;

/// <summary>
/// The Android host's saved-replay playback: Core's <see cref="SavedReplayPlayback"/> (ADR-0020)
/// over <see cref="AndroidSavedReplayHost"/> (the host records no video and has no
/// current-replay recording). The module owns every start, exit and menu-return decision; this
/// runtime wires the game to it: <c>ReplayState</c> transitions arrive through
/// <c>Events.StateChanged</c>, the bootstrapped <c>ReplayState.Exit</c> prefix routes into
/// <see cref="TryExitBootstrappedSavedReplayToMenu"/> (ADR-0002), the replay guard reads the
/// module, and the melon's update ticks it. A plain object, not a MonoBehaviour.
/// </summary>
internal sealed class AndroidCombatReplayRuntime : IDisposable
{
    private readonly AndroidSavedReplayHost _host;
    private Il2CppSystem.Action<StateChangedEvent>? _stateListener;
    private bool _disposed;

    public AndroidCombatReplayRuntime(
        IBppServices services,
        RunLifecycleModule runLifecycle,
        ReplayPayloadOperationGate gate,
        SavedReplayCatalog saved
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        _host = new AndroidSavedReplayHost(saved);
        SavedReplays = new SavedReplayPlayback(
            services,
            runLifecycle,
            new ReplayPlaybackPublisher(services),
            gate,
            saved,
            _host
        );
    }

    /// <summary>The runtime the replay patches reach; null before start and after dispose.</summary>
    public static AndroidCombatReplayRuntime? Instance { get; private set; }

    /// <summary>Saved Replay Playback, the runtime History replays through.</summary>
    public SavedReplayPlayback SavedReplays { get; }

    public long Starts => _host.Starts;

    public long StartFailures => _host.StartFailures;

    public long ExitsHandled => _host.ExitsHandled;

    public long MenuReturns => _host.MenuReturns;

    /// <summary>Main thread: listens for <c>ReplayState</c> transitions and arms the guards.</summary>
    public void Start()
    {
        Instance = this;
        AndroidReplayGuard.IsActiveSource = () =>
            SavedReplays.IsReplayStartInProgress || SavedReplays.IsSavedReplayPlaybackActive;
        try
        {
            _stateListener =
                DelegateSupport.ConvertDelegate<Il2CppSystem.Action<StateChangedEvent>>(
                    new Action<StateChangedEvent>(OnStateChanged)
                ) ?? throw new InvalidOperationException("Il2CppInterop returned no delegate.");
            Events.StateChanged.AddListener(_stateListener, null);
        }
        catch (Exception exception)
        {
            // Without the listener, the ReplayState-gone poll in Tick still clears the latch, but
            // a native recap exit would not complete its operation and no state_observed is
            // logged: log it for the device run.
            _stateListener = null;
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.replay.state_listener_failed"),
                exception
            );
        }
    }

    /// <summary>Main thread, every frame.</summary>
    public void Tick()
    {
        if (_disposed)
            return;
        using var scope = AndroidMainThread.Host.Enter();
        try
        {
            SavedReplays.Tick();
        }
        catch (Exception exception)
        {
            BppLog.WarnEvent(
                new BppLogEvent(
                    BppLogFeatureScope.Android,
                    "android.replay.tick_failed",
                    storm: ["reason_code"]
                ),
                exception,
                ("reason_code", exception.GetType().Name)
            );
        }
    }

    /// <summary>
    /// The bootstrapped <c>ReplayState.Exit</c> prefix: true when the runtime handled the exit
    /// (the original body is skipped), false to let the game exit as usual.
    /// </summary>
    public static bool TryExitBootstrappedSavedReplayToMenu() =>
        Instance is { _disposed: false } runtime && runtime.SavedReplays.TryExitBootstrapped();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SavedReplays.Dispose();
        if (ReferenceEquals(Instance, this))
            Instance = null;
        AndroidReplayGuard.IsActiveSource = null;
        if (_stateListener != null)
        {
            try
            {
                Events.StateChanged.RemoveListener(_stateListener);
            }
            catch
            {
                // The game may already be tearing its events down at quit.
            }
            _stateListener = null;
        }
    }

    private void OnStateChanged(StateChangedEvent data)
    {
        if (_disposed || data == null)
            return;
        using var scope = AndroidMainThread.Host.Enter();
        try
        {
            var wasReplay = data.PreviousState?.TryCast<ReplayState>() != null;
            var isReplay = data.CurrentState?.TryCast<ReplayState>() != null;
            // Logged on the transition rather than polled every frame; the session is the one
            // the transition finds, before a state exit settles it.
            if (wasReplay != isReplay)
                BppLog.InfoEvent(
                    new BppLogEvent(BppLogFeatureScope.Android, "android.replay.state_observed"),
                    ("in_replay_state", isReplay),
                    ("session", SavedReplays.HasSavedReplaySession),
                    ("scene", AndroidLobbyScene.Describe())
                );
            if (wasReplay && !isReplay)
                SavedReplays.OnReplayStateExited();
        }
        catch (Exception exception)
        {
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.replay.state_exit_failed"),
                exception
            );
        }
    }
}
