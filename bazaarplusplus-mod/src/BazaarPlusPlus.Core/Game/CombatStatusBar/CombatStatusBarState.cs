#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using BazaarPlusPlus.Core.RunContext;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.CombatStatusBar;

/// <summary>Combat playback progress, speed, and pause as the combat status bar shows them.</summary>
public sealed class CombatStatusBarState
{
    private static readonly float[] SpeedSteps = { 0.5f, 0.67f, 1f };

    private readonly ICombatPauseControl _pauseControl;
    private ISetting<float>? _speedSetting;
    private string? _cachedElapsedText;
    private long _cachedElapsedTicks;

    public CombatStatusBarState(ICombatPauseControl pauseControl)
    {
        _pauseControl = pauseControl ?? throw new ArgumentNullException(nameof(pauseControl));
    }

    public bool IsCombatPlaybackActive { get; private set; }
    public bool IsCombatPaused { get; private set; }
    public float CombatSpeedMultiplier { get; private set; } = 1f;
    public int ProcessedCombatFrames { get; private set; }
    public int TotalCombatFrames { get; private set; }
    public TimeSpan LastCombatLogicalElapsed { get; private set; }
    public bool HasCompletedCombatPlayback { get; private set; }

    /// <summary>
    /// Loads the preferred speed from <paramref name="speedSetting"/> and persists later speed
    /// changes to it. Only the first call binds; later calls are no-ops.
    /// </summary>
    public void BindSpeedSetting(ISetting<float> speedSetting)
    {
        if (_speedSetting != null)
            return;

        _speedSetting = speedSetting ?? throw new ArgumentNullException(nameof(speedSetting));
        CombatSpeedMultiplier = NormalizeConfiguredDefaultSpeed(speedSetting.Value);
        BppLog.DebugEvent(
            new BppLogEvent(BppLogFeatureScope.CombatStatusBar, "combat_status_bar.config.loaded"),
            () => [("speed_multiplier", ToLogCategory(CombatSpeedMultiplier))]
        );
    }

    public void BeginCombatPlayback()
    {
        IsCombatPlaybackActive = true;
        ProcessedCombatFrames = 0;
    }

    public void EndCombatPlayback()
    {
        LastCombatLogicalElapsed = GetCombatLogicalElapsed();
        HasCompletedCombatPlayback = true;
        if (IsCombatPaused)
            SetCombatPaused(false);
        IsCombatPlaybackActive = false;
    }

    /// <summary>A scene left playback; no previous battle time belongs to the new scene.</summary>
    public void ResetPlayback()
    {
        if (IsCombatPaused)
            SetCombatPaused(false);
        IsCombatPlaybackActive = false;
        HasCompletedCombatPlayback = false;
        ProcessedCombatFrames = 0;
        TotalCombatFrames = 0;
        LastCombatLogicalElapsed = TimeSpan.Zero;
    }

    public void SetCombatFrameTotal(int totalFrames)
    {
        TotalCombatFrames = Math.Max(totalFrames, 0);
        ProcessedCombatFrames = 0;
    }

    public void AdvanceCombatFrame()
    {
        if (!IsCombatPlaybackActive)
            return;

        ProcessedCombatFrames++;
        if (TotalCombatFrames > 0 && ProcessedCombatFrames > TotalCombatFrames)
            ProcessedCombatFrames = TotalCombatFrames;
    }

    public TimeSpan GetCombatLogicalElapsed()
    {
        return TimeSpan.FromMilliseconds(GetCurrentCombatFrameIndex() * 50d);
    }

    public float CycleCombatSpeed() =>
        SetCombatSpeed(SpeedSteps[(GetCurrentSpeedStepIndex() + 1) % SpeedSteps.Length]);

    public float SetCombatSpeed(float speed)
    {
        if (!IsSupportedSpeedStep(speed))
            return CombatSpeedMultiplier;

        CombatSpeedMultiplier = speed;
        if (_speedSetting != null)
            _speedSetting.Value = speed;
        return CombatSpeedMultiplier;
    }

    public bool ShouldOverrideCombatSpeed(float requestedSpeed)
    {
        if (!IsCombatPlaybackActive)
            return false;

        return requestedSpeed <= 1f + 0.0001f;
    }

    public void OnVisibilitySettingChanged(bool enabled)
    {
        // A hidden pause button must not leave a combat paused by the status bar.
        if (!enabled && IsCombatPaused)
            SetCombatPaused(false);
    }

    public string GetDisplayedTimeText()
    {
        return IsCombatPlaybackActive ? FormatElapsedCached(GetCombatLogicalElapsed())
            : HasCompletedCombatPlayback ? FormatElapsedCached(LastCombatLogicalElapsed)
            : "0:00.00";
    }

    public bool CanToggleCombatPause()
    {
        return IsCombatPlaybackActive && _pauseControl.IsAvailable;
    }

    public bool ToggleCombatPause()
    {
        return SetCombatPaused(!IsCombatPaused);
    }

    public bool SetCombatPaused(bool paused)
    {
        if (_pauseControl.TrySetPaused(paused, out var isPaused))
            IsCombatPaused = isPaused;

        return IsCombatPaused;
    }

    public static bool ShouldRenderForState(
        bool enabled,
        IRunContext runContext,
        IGameStateProbe gameStateProbe
    )
    {
        if (!enabled)
            return false;

        // Fast path: during a live run the cached flag is already true, so the per-frame probe
        // never runs. The cache is only false in lobby/menu and during replay playback — replay
        // enters ReplayState via AppState.TryPushState without firing RunStarted, so nothing ever
        // flips the cache true. Consult the live probe in that case; ComputeIsInGameRun() reports
        // ReplayState as in-game-run, which keeps the bar (and its playback controls) visible.
        if (runContext.IsInGameRun)
            return true;

        try
        {
            return gameStateProbe.ComputeIsInGameRun();
        }
        catch
        {
            // Probe touches game statics; never log here (per-frame call would spam). Hide if unsure.
            return false;
        }
    }

    public static float NormalizeConfiguredDefaultSpeed(float configuredSpeed)
    {
        return IsSupportedSpeedStep(configuredSpeed) ? configuredSpeed : 1f;
    }

    public static float AdvanceVisualBlend(float current, bool active, float deltaTime)
    {
        var target = active ? 1f : 0f;
        var maxStep = Math.Max(deltaTime, 0f) * 5f;
        if (current < target)
            return Math.Min(current + maxStep, target);

        if (current > target)
            return Math.Max(current - maxStep, target);

        return current;
    }

    private string FormatElapsedCached(TimeSpan elapsed)
    {
        if (_cachedElapsedText == null || _cachedElapsedTicks != elapsed.Ticks)
        {
            _cachedElapsedTicks = elapsed.Ticks;
            _cachedElapsedText = FormatElapsed(elapsed);
        }

        return _cachedElapsedText;
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        var minutes = (int)elapsed.TotalMinutes;
        return $"{minutes}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 10:00}";
    }

    [ThreadStatic]
    private static System.Text.StringBuilder? _tabularBuffer;

    /// <summary>
    /// <paramref name="time"/> with each run of ASCII digits in a TMP <c>mspace</c> tag, so the
    /// digits share one advance and the time never shifts while the separators keep their own
    /// width. The time text above writes no other digits.
    /// </summary>
    public static string WithTabularDigits(string time)
    {
        var buffer = _tabularBuffer ??= new System.Text.StringBuilder(64);
        buffer.Clear();
        var inDigits = false;
        foreach (var c in time)
        {
            var digit = c >= '0' && c <= '9';
            if (digit && !inDigits)
                buffer.Append("<mspace=0.55em>");
            else if (!digit && inDigits)
                buffer.Append("</mspace>");
            buffer.Append(c);
            inDigits = digit;
        }
        if (inDigits)
            buffer.Append("</mspace>");
        return buffer.ToString();
    }

    private int GetCurrentCombatFrameIndex()
    {
        return Math.Max(ProcessedCombatFrames - 1, 0);
    }

    private int GetCurrentSpeedStepIndex()
    {
        var currentIndex = 0;
        var smallestDelta = float.MaxValue;
        for (var i = 0; i < SpeedSteps.Length; i++)
        {
            var delta = Math.Abs(SpeedSteps[i] - CombatSpeedMultiplier);
            if (delta < smallestDelta)
            {
                smallestDelta = delta;
                currentIndex = i;
            }
        }

        return currentIndex;
    }

    private static bool IsSupportedSpeedStep(float speed)
    {
        for (var i = 0; i < SpeedSteps.Length; i++)
        {
            if (Math.Abs(SpeedSteps[i] - speed) < 0.0001f)
                return true;
        }

        return false;
    }

    private static CombatSpeedLogCategory ToLogCategory(float speed)
    {
        if (Math.Abs(speed - 0.5f) < 0.001f)
            return CombatSpeedLogCategory.Half;
        if (Math.Abs(speed - 0.67f) < 0.001f)
            return CombatSpeedLogCategory.TwoThirds;
        if (Math.Abs(speed - 1f) < 0.001f)
            return CombatSpeedLogCategory.Normal;
        return CombatSpeedLogCategory.Custom;
    }
}
