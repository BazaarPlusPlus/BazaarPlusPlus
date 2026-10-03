#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.GameInterop.GraphicsUpscaling;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;

namespace BazaarPlusPlus.Game.GraphicsUpscaling;

internal sealed class GraphicsUpscalingController : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.5f;

    private readonly UrpUpscalingAdapter _adapter = new();
    private BppConfig? _config;
    private UrpUpscalingState? _lastLoggedState;
    private float _nextRefreshAt;
    private bool _unavailableLogged;

    internal void Initialize(BppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        Refresh();
    }

    private void Update()
    {
        if (_config == null || Time.unscaledTime < _nextRefreshAt)
            return;
        Refresh();
    }

    private void OnDestroy() => _adapter.Dispose();

    private void Refresh()
    {
        _nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
        var mode = _config?.GraphicsUpscalingModeConfig.Value ?? GraphicsUpscalingMode.Native;
        var sharpness =
            _config?.GraphicsUpscalingSharpnessConfig.Value ?? BppConfig.DefaultFsrSharpness;
        var profile = FsrUpscalingProfiles.Resolve(mode);
        var state = _adapter.Apply(profile.Enabled, profile.RenderScale, sharpness);

        if (!state.AssetAvailable)
        {
            if (!_unavailableLogged && mode != GraphicsUpscalingMode.Native)
            {
                BppLog.WarnEvent(
                    new BppLogEvent(
                        BppLogFeatureScope.GraphicsUpscaling,
                        "graphics_upscaling.runtime.unavailable",
                        storm: ["mode"]
                    ),
                    ("mode", mode)
                );
                _unavailableLogged = true;
            }
            return;
        }

        // Only a degraded episode has a storm to recover; this refresh runs at 2 Hz forever.
        if (_unavailableLogged)
        {
            _unavailableLogged = false;
            BppLog.RecoverStorm(
                new BppLogEvent(
                    BppLogFeatureScope.GraphicsUpscaling,
                    "graphics_upscaling.runtime.unavailable",
                    storm: ["mode"]
                )
            );
        }

        if (_lastLoggedState == state)
            return;

        _lastLoggedState = state;
        BppLog.InfoEvent(
            new BppLogEvent(
                BppLogFeatureScope.GraphicsUpscaling,
                "graphics_upscaling.state.applied"
            ),
            ("mode", mode),
            ("effective_filter", state.EffectiveFilter),
            ("render_scale", state.RenderScale),
            ("render_pixel_ratio", state.RenderScale * state.RenderScale),
            ("fsr_sharpness", state.FsrSharpness),
            ("output_resolution", $"{state.OutputWidth}x{state.OutputHeight}"),
            ("internal_resolution", $"{state.InternalWidth}x{state.InternalHeight}"),
            ("dynamic_buffer_scale", $"{state.DynamicWidthScale:F2}x{state.DynamicHeightScale:F2}")
        );
    }
}
