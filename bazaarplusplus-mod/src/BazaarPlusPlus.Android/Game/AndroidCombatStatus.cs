using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.CombatStatusBar;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppInterop.Runtime;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.Game;

/// <summary>The native playback signals feeding the same logical clock as the desktop.</summary>
internal sealed class AndroidCombatStatus : IDisposable
{
    private readonly Il2CppSystem.Action _started;
    private readonly Il2CppSystem.Action _ended;
    public CombatStatusBarState State { get; } = new(new PauseControl());
    public BppSettings Settings { get; }

    public AndroidCombatStatus(BppSettings settings)
    {
        Settings = settings;
        State.BindSpeedSetting(settings.CombatStatusBarSpeedMultiplierConfig);
        _started =
            DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                (Action)(
                    () =>
                    {
                        State.BeginCombatPlayback();
                        Log("started");
                    }
                )
            ) ?? throw new InvalidOperationException("Could not bind combat listener.");
        _ended =
            DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                (Action)(
                    () =>
                    {
                        State.EndCombatPlayback();
                        Log("ended");
                    }
                )
            ) ?? throw new InvalidOperationException("Could not bind combat listener.");
        Events.CombatStarted.AddListener(_started, null);
        Events.CombatEnded.AddListener(_ended, null);
    }

    public void Reset()
    {
        if (State.IsCombatPlaybackActive || State.IsCombatPaused)
            State.EndCombatPlayback();
        State.ResetPlayback();
    }

    public void Dispose()
    {
        Events.CombatStarted.RemoveListener(_started);
        Events.CombatEnded.RemoveListener(_ended);
        Reset();
    }

    public void TogglePause()
    {
        if (!State.CanToggleCombatPause())
            return;
        State.ToggleCombatPause();
        Log("pause_changed");
    }

    private void Log(string phase) =>
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.combat_status.changed"),
            ("phase", phase),
            ("time", State.GetDisplayedTimeText()),
            ("frames", State.ProcessedCombatFrames),
            ("total", State.TotalCombatFrames),
            ("paused", State.IsCombatPaused)
        );

    private sealed class PauseControl : ICombatPauseControl
    {
        public bool IsAvailable => Il2Cpp.Singleton<Il2Cpp.GameServiceManager>.Instance != null;

        public bool TrySetPaused(bool paused, out bool isPaused)
        {
            var manager = Il2Cpp.Singleton<Il2Cpp.GameServiceManager>.Instance;
            isPaused = false;
            if (manager == null)
                return false;
            manager.PauseOrUnpauseGame(paused);
            isPaused = manager.GamePaused;
            return true;
        }
    }
}
