#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.GameInterop.GraphicsUpscaling;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.GraphicsUpscaling;

internal sealed class GraphicsUpscalingController : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.5f;

    private readonly UrpUpscalingAdapter _adapter = new();
    private IBppConfig? _config;
    private UrpUpscalingState? _lastLoggedState;
    private float _nextRefreshAt;
    private bool _unavailableLogged;

    internal void Initialize(IBppConfig config)
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
        var mode = _config?.GraphicsUpscalingModeConfig?.Value ?? GraphicsUpscalingMode.Native;
        var sharpness =
            _config?.GraphicsUpscalingSharpnessConfig?.Value ?? BppConfig.DefaultFsrSharpness;
        var profile = FsrUpscalingProfiles.Resolve(mode);
        var state = _adapter.Apply(profile.Enabled, profile.RenderScale, sharpness);

        if (!state.AssetAvailable)
        {
            if (!_unavailableLogged && mode != GraphicsUpscalingMode.Native)
            {
                BppLog.WarnEvent(
                    GraphicsUpscalingLogEvents.Unavailable,
                    GraphicsUpscalingLogEvents.UnavailableMode.Bind(mode)
                );
                _unavailableLogged = true;
            }
            return;
        }

        _unavailableLogged = false;
        BppLog.RecoverStorm(GraphicsUpscalingLogEvents.Unavailable);
        if (_lastLoggedState == state)
            return;

        _lastLoggedState = state;
        BppLog.InfoEvent(
            GraphicsUpscalingLogEvents.Applied,
            GraphicsUpscalingLogEvents.AppliedMode.Bind(mode),
            GraphicsUpscalingLogEvents.AppliedEffectiveFilter.Bind(state.EffectiveFilter),
            GraphicsUpscalingLogEvents.AppliedRenderScale.Bind(state.RenderScale),
            GraphicsUpscalingLogEvents.AppliedRenderPixelRatio.Bind(
                state.RenderScale * state.RenderScale
            ),
            GraphicsUpscalingLogEvents.AppliedFsrSharpness.Bind(state.FsrSharpness),
            GraphicsUpscalingLogEvents.AppliedOutputResolution.Bind(
                $"{state.OutputWidth}x{state.OutputHeight}"
            ),
            GraphicsUpscalingLogEvents.AppliedInternalResolution.Bind(
                $"{state.InternalWidth}x{state.InternalHeight}"
            ),
            GraphicsUpscalingLogEvents.AppliedDynamicBufferScale.Bind(
                $"{state.DynamicWidthScale:F2}x{state.DynamicHeightScale:F2}"
            )
        );
    }
}
