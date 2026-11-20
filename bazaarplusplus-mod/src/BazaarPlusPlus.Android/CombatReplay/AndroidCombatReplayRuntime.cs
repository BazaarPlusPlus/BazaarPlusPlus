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

internal sealed class AndroidCombatReplayRuntime : IDisposable
{
    private readonly AndroidSavedReplayHost _host;
    private Il2CppSystem.Action<StateChangedEvent>? _stateListener;
    private bool? _loggedInReplayState;
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

    public static AndroidCombatReplayRuntime? Instance { get; private set; }

    public SavedReplayPlayback SavedReplays { get; }

    public long Starts => _host.Starts;

    public long StartFailures => _host.StartFailures;

    public long ExitsHandled => _host.ExitsHandled;

    public long MenuReturns => _host.MenuReturns;

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
            // The exit poll can clear the latch after a listener failure, but native recap exit
            // still needs the state event to finish.
            _stateListener = null;
            BppLog.ErrorEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.replay.state_listener_failed"),
                exception
            );
        }
    }

    public void Tick()
    {
        if (_disposed)
            return;
        using var scope = AndroidMainThread.Host.Enter();
        try
        {
            if (SavedReplays.Tick())
                LogReplayState(false, "exit_latch_cleared");
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

    // Bootstrapped exit can leave ReplayState without a state event. The exit-latch poll catches
    // that path.
    private void LogReplayState(bool inReplayState, string signal)
    {
        if (_loggedInReplayState == inReplayState)
            return;
        _loggedInReplayState = inReplayState;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.replay.state_observed"),
            ("in_replay_state", inReplayState),
            ("signal", signal),
            ("session", SavedReplays.HasSavedReplaySession),
            ("scene", AndroidLobbyScene.Describe())
        );
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
            if (wasReplay != isReplay)
                LogReplayState(isReplay, "state_changed");
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
