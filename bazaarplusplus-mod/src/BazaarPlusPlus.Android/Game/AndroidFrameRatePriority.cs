using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using Il2CppTheBazaar.QualityFramework;
using UnityEngine;

namespace BazaarPlusPlus.Android.Game;

// The mod's own main-thread work can trigger the game's frame-cap scaler. Adaptive Performance uses
// separate scalers.
internal sealed class AndroidFrameRatePriority
{
    private const float IntervalSeconds = 1f;
    private const int MaxPreferenceFrameRate = 120;

    private readonly Func<bool> _enabled;
    private readonly List<QualitySettingsSO.QualitySettings> _held = new();
    private PerformanceController? _controller;
    private float _nextAt;

    public AndroidFrameRatePriority(Func<bool> enabled) =>
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));

    public void Tick(float now)
    {
        if (now < _nextAt)
            return;
        _nextAt = now + IntervalSeconds;

        var controller = Controller();
        if (controller == null)
            return;
        if (_enabled())
            Hold(controller);
        else
            Release(controller);
    }

    private PerformanceController? Controller()
    {
        if (_controller != null)
            return _controller;
        var found = UnityEngine.Object.FindFirstObjectByType<PerformanceController>();
        if (found == null || !found._isInitialized)
            return null;
        _controller = found;
        var settings = found._currentQualitySetting;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.frame_rate.controller_found"),
            ("adaptive_performance", found._adaptivePerformanceIsActive),
            ("quality", settings?.GameQuality.ToString() ?? "none"),
            ("dynamic_frame_capping", settings?.DynamicFrameCapping ?? false),
            ("dynamic_resolution", settings?.DynamicScaleResolution ?? false),
            ("min_frame_rate", settings?.MinFrameRate ?? 0),
            ("frame_rate_steps", settings?.FrameRateSteps ?? 0),
            ("min_resolution", settings?.MinResolution ?? 0f),
            ("target_frame_rate", Application.targetFrameRate),
            ("preference_frame_rate", PreferenceFrameRate())
        );
        return found;
    }

    private void Hold(PerformanceController controller)
    {
        if (controller._adaptivePerformanceIsActive)
            return;
        var settings = controller._currentQualitySetting;
        if (settings == null)
            return;

        var target = PreferenceFrameRate();
        if (settings.DynamicFrameCapping)
        {
            var dropped = Application.targetFrameRate;
            settings.DynamicFrameCapping = false;
            if (!_held.Any(held => held.Pointer == settings.Pointer))
                _held.Add(settings);
            // ApplySettings destroys the scaler, whose OnDestroy restores the cap it captured in
            // Awake, potentially before preferences loaded.
            if (controller._frameCappingScaler is { } scaler && target > 0)
                scaler._defaultFps = target;
            controller.ApplySettings();
            BppLog.InfoEvent(
                new BppLogEvent(BppLogFeatureScope.Android, "android.frame_rate.held"),
                ("quality", settings.GameQuality.ToString()),
                ("dropped_frame_rate", dropped),
                ("target_frame_rate", target)
            );
        }

        if (target <= 0)
            return;
        if (Application.targetFrameRate != target)
            Application.targetFrameRate = target;
        // The resolution scaler measures the GPU against this; the frame-cap scaler left it at
        // the last frame rate it set.
        var frameTimeMs = 1000f / target;
        if (Math.Abs(controller._targetFrameTimeing - frameTimeMs) > 0.01f)
            controller._targetFrameTimeing = frameTimeMs;
    }

    private void Release(PerformanceController controller)
    {
        if (_held.Count == 0)
            return;
        foreach (var settings in _held)
            settings.DynamicFrameCapping = true;
        _held.Clear();
        controller.ApplySettings();
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.frame_rate.released"),
            ("target_frame_rate", Application.targetFrameRate)
        );
    }

    // Avoid the UI-scale notification raised by PlayerPreferences.Apply.
    private static int PreferenceFrameRate()
    {
        var data = Il2Cpp.PlayerPreferences.Data;
        if (data == null)
            return 0;
        if (data.VideoSynchronization)
            return (int)Math.Round(Screen.currentResolution.refreshRateRatio.value);
        return Math.Min(data.TargetFramerate, MaxPreferenceFrameRate);
    }
}
