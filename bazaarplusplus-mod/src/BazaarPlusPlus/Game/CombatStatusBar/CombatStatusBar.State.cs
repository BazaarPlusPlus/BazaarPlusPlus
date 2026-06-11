#nullable enable
using System;
using BazaarPlusPlus.Core.Runtime;
using TheBazaar;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal enum CombatStatusBarButtonKind
{
    Speed,
    Pause,
}

internal readonly record struct CombatStatusBarRgba(float R, float G, float B, float A)
{
    internal CombatStatusBarRgba WithAlpha(float alpha)
    {
        return new CombatStatusBarRgba(R, G, B, alpha);
    }
}

internal readonly record struct CombatStatusBarButtonPalette(
    CombatStatusBarRgba Normal,
    CombatStatusBarRgba Pressed,
    CombatStatusBarRgba Unavailable,
    CombatStatusBarRgba Text
);

internal readonly record struct CombatStatusBarButtonVisuals(
    bool Interactable,
    CombatStatusBarRgba Normal,
    CombatStatusBarRgba Pressed,
    CombatStatusBarRgba Disabled,
    CombatStatusBarRgba Background,
    CombatStatusBarRgba Text
);

internal sealed partial class CombatStatusBar
{
    private static readonly float[] SpeedSteps = { 0.5f, 0.67f, 1f };

    private static string? _cachedElapsedText;
    private static long _cachedElapsedTicks;

    internal static bool IsCombatPlaybackActive { get; private set; }
    internal static bool IsCombatPaused { get; private set; }
    internal static float CombatSpeedMultiplier { get; private set; } = 1f;
    internal static int ProcessedCombatFrames { get; private set; }
    internal static int TotalCombatFrames { get; private set; }
    internal static TimeSpan LastCombatLogicalElapsed { get; private set; }
    internal static bool HasCompletedCombatPlayback { get; private set; }
    internal static ReadOnlySpan<float> CombatSpeedSteps => SpeedSteps;

    internal static void BeginCombatPlayback()
    {
        IsCombatPlaybackActive = true;
        ProcessedCombatFrames = 0;
    }

    internal static void EndCombatPlayback()
    {
        LastCombatLogicalElapsed = GetCombatLogicalElapsed();
        HasCompletedCombatPlayback = true;
        SetCombatPaused(false);
        IsCombatPlaybackActive = false;
    }

    internal static void SetCombatFrameTotal(int totalFrames)
    {
        TotalCombatFrames = Math.Max(totalFrames, 0);
        ProcessedCombatFrames = 0;
    }

    internal static void AdvanceCombatFrame()
    {
        if (!IsCombatPlaybackActive)
            return;

        ProcessedCombatFrames++;
        if (TotalCombatFrames > 0 && ProcessedCombatFrames > TotalCombatFrames)
            ProcessedCombatFrames = TotalCombatFrames;
    }

    internal static TimeSpan GetCombatLogicalElapsed()
    {
        return TimeSpan.FromMilliseconds(GetCurrentCombatFrameIndex() * 50d);
    }

    internal static float StepCombatSpeed(int direction)
    {
        var currentIndex = GetCurrentSpeedStepIndex();
        currentIndex = Math.Clamp(currentIndex + direction, 0, SpeedSteps.Length - 1);
        return SetCombatSpeed(SpeedSteps[currentIndex]);
    }

    internal static float SetCombatSpeed(float speed)
    {
        if (!IsSupportedSpeedStep(speed))
            return CombatSpeedMultiplier;

        CombatSpeedMultiplier = speed;
        PersistCombatSpeed(speed);
        return CombatSpeedMultiplier;
    }

    internal static bool ShouldOverrideCombatSpeed(float requestedSpeed)
    {
        if (!IsCombatPlaybackActive)
            return false;

        return requestedSpeed <= 1f + 0.0001f;
    }

    internal static bool ShouldRenderForState(bool enabled)
    {
        if (!enabled || _services == null)
            return false;

        // Fast path: during a live run the cached flag is already true, so the per-frame probe
        // never runs. The cache is only false in lobby/menu and during replay playback — replay
        // enters ReplayState via AppState.TryPushState without firing RunStarted, so nothing ever
        // flips the cache true. Consult the live probe in that case; ComputeIsInGameRun() reports
        // ReplayState as in-game-run, which keeps the bar (and its playback controls) visible.
        if (_services.RunContext.IsInGameRun)
            return true;

        try
        {
            return _services.GameStateProbe.ComputeIsInGameRun();
        }
        catch
        {
            // Probe touches game statics; never log here (per-frame call would spam). Hide if unsure.
            return false;
        }
    }

    internal static bool CanStepCombatSpeed(int direction)
    {
        var currentIndex = GetCurrentSpeedStepIndex();
        var nextIndex = currentIndex + direction;
        return nextIndex >= 0 && nextIndex < SpeedSteps.Length;
    }

    internal static float NormalizeConfiguredDefaultSpeed(float configuredSpeed)
    {
        return IsSupportedSpeedStep(configuredSpeed) ? configuredSpeed : 1f;
    }

    internal static string GetDisplayedTimeLabel()
    {
        return IsCombatPlaybackActive ? "Time" : "LastCombat";
    }

    internal static string GetDisplayedTimeText()
    {
        return IsCombatPlaybackActive ? FormatElapsedCached(GetCombatLogicalElapsed())
            : HasCompletedCombatPlayback ? FormatElapsedCached(LastCombatLogicalElapsed)
            : "-:--:--";
    }

    internal static float AdvanceVisualBlend(float current, bool active, float deltaTime)
    {
        var target = active ? 1f : 0f;
        var maxStep = Math.Max(deltaTime, 0f) * 5f;
        if (current < target)
            return Math.Min(current + maxStep, target);

        if (current > target)
            return Math.Max(current - maxStep, target);

        return current;
    }

    internal static void ResetStateForTests()
    {
        IsCombatPlaybackActive = false;
        IsCombatPaused = false;
        CombatSpeedMultiplier = 1f;
        ProcessedCombatFrames = 0;
        TotalCombatFrames = 0;
        LastCombatLogicalElapsed = TimeSpan.Zero;
        HasCompletedCombatPlayback = false;
    }

    internal static bool CanToggleCombatPause()
    {
        return IsCombatPlaybackActive && Singleton<GameServiceManager>.Instance != null;
    }

    internal static bool ToggleCombatPause()
    {
        return SetCombatPaused(!IsCombatPaused);
    }

    internal static bool SetCombatPaused(bool paused)
    {
        var gameServiceManager = Singleton<GameServiceManager>.Instance;
        if (gameServiceManager == null)
            return IsCombatPaused;

        gameServiceManager.PauseOrUnpauseGame(paused);
        IsCombatPaused = gameServiceManager.GamePaused;
        return IsCombatPaused;
    }

    internal static CombatStatusBarButtonVisuals ResolveButtonVisuals(
        CombatStatusBarButtonKind kind,
        bool interactable,
        CombatStatusBarButtonPalette palette
    )
    {
        var disabledColor =
            kind == CombatStatusBarButtonKind.Pause ? palette.Normal : palette.Unavailable;
        var textColor =
            interactable || kind == CombatStatusBarButtonKind.Pause
                ? palette.Text
                : palette.Text.WithAlpha(0.45f);

        return new CombatStatusBarButtonVisuals(
            Interactable: interactable,
            Normal: palette.Normal,
            Pressed: palette.Pressed,
            Disabled: disabledColor,
            Background: interactable ? palette.Normal : disabledColor,
            Text: textColor
        );
    }

    private static string FormatElapsedCached(TimeSpan elapsed)
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
        return $"{minutes}:{elapsed.Seconds:00}:{elapsed.Milliseconds / 10:00}";
    }

    private static int GetCurrentCombatFrameIndex()
    {
        return Math.Max(ProcessedCombatFrames - 1, 0);
    }

    private static int GetCurrentSpeedStepIndex()
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

    static partial void PersistCombatSpeed(float speed);
}
