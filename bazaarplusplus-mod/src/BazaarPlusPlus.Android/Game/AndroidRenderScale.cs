using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BazaarPlusPlus.Android.Game;

// The game never writes the URP asset's renderScale. Its own resolution scaler multiplies on top of
// it through AdaptivePerformanceRenderSettings.RenderScaleMultiplier, which URP reads only when the
// asset's useAdaptivePerformance is on.
internal sealed class AndroidRenderScale : IDisposable
{
    public const int NativePercent = 100;
    private const float IntervalSeconds = 1f;

    private readonly Func<int> _percent;
    private UniversalRenderPipelineAsset? _asset;
    private float _original;
    private float _nextAt;

    public AndroidRenderScale(Func<int> percent) =>
        _percent = percent ?? throw new ArgumentNullException(nameof(percent));

    public void Tick(float now)
    {
        if (now < _nextAt)
            return;
        _nextAt = now + IntervalSeconds;

        var percent = Math.Clamp(_percent(), 1, NativePercent);
        if (percent == NativePercent && _asset is null)
            return;
        var current =
            GraphicsSettings.currentRenderPipeline?.TryCast<UniversalRenderPipelineAsset>();
        if (
            _asset is not null
            && (percent == NativePercent || current == null || current.Pointer != _asset.Pointer)
        )
            Restore();
        if (percent == NativePercent || current == null)
            return;

        if (_asset is null)
        {
            _asset = current;
            _original = current.renderScale;
        }
        var target = percent / 100f;
        if (Math.Abs(current.renderScale - target) < 0.001f)
            return;
        current.renderScale = target;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.render_scale.applied"),
            ("render_scale", target),
            ("original_render_scale", _original),
            ("use_adaptive_performance", current.useAdaptivePerformance),
            ("screen", $"{Screen.width}x{Screen.height}")
        );
    }

    public void Dispose() => Restore();

    private void Restore()
    {
        if (_asset is null)
            return;
        // A destroyed asset compares equal to null and takes no write.
        if (_asset != null)
            _asset.renderScale = _original;
        BppLog.InfoEvent(
            new BppLogEvent(BppLogFeatureScope.Android, "android.render_scale.restored"),
            ("render_scale", _original)
        );
        _asset = null;
    }
}
