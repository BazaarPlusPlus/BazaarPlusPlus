#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarPlusPlus.GameInterop.GraphicsUpscaling;

internal sealed class UrpUpscalingAdapter : IDisposable
{
    private const string UniversalPipelineAssetTypeName =
        "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";
    private UrpAssetSurface? _modifiedAsset;
    private AssetSnapshot _original;

    internal UrpUpscalingState Apply(bool enabled, float renderScale, float fsrSharpness)
    {
        var currentAsset = UrpAssetSurface.TryCreate(GraphicsSettings.currentRenderPipeline);
        if (currentAsset == null)
        {
            Restore();
            return Unavailable(fsrSharpness);
        }

        if (!enabled)
        {
            Restore();
            currentAsset = UrpAssetSurface.TryCreate(GraphicsSettings.currentRenderPipeline);
            return currentAsset == null ? Unavailable(fsrSharpness) : Observe(currentAsset);
        }

        if (
            _modifiedAsset == null
            || !_modifiedAsset.IsAlive
            || _modifiedAsset.InstanceId != currentAsset.InstanceId
        )
        {
            Restore();
            _modifiedAsset = currentAsset;
            _original = AssetSnapshot.Capture(currentAsset);
        }

        currentAsset.RenderScale = Mathf.Clamp(renderScale, 0.1f, 1f);
        currentAsset.Filter = UrpUpscalingFilter.Fsr;
        currentAsset.FsrOverrideSharpness = true;
        currentAsset.FsrSharpness = Mathf.Clamp01(fsrSharpness);

        // The game's separate dynamic-resolution scaler is disabled in the current quality
        // assets. Keep its multiplier neutral so a future quality preset cannot
        // accidentally apply a second scale on top of the explicit URP render scale.
        if (
            !Mathf.Approximately(ScalableBufferManager.widthScaleFactor, 1f)
            || !Mathf.Approximately(ScalableBufferManager.heightScaleFactor, 1f)
        )
        {
            ScalableBufferManager.ResizeBuffers(1f, 1f);
        }

        return Observe(currentAsset);
    }

    public void Dispose() => Restore();

    private void Restore()
    {
        if (_modifiedAsset == null)
            return;

        if (_modifiedAsset.IsAlive)
            _original.Restore(_modifiedAsset);
        ScalableBufferManager.ResizeBuffers(
            _original.DynamicWidthScale,
            _original.DynamicHeightScale
        );
        _modifiedAsset = null;
        _original = default;
    }

    private static UrpUpscalingState Observe(UrpAssetSurface asset)
    {
        var outputWidth = Screen.width;
        var outputHeight = Screen.height;
        var scale = asset.RenderScale;
        return new UrpUpscalingState(
            true,
            asset.Filter,
            scale,
            asset.FsrSharpness,
            outputWidth,
            outputHeight,
            Mathf.CeilToInt(outputWidth * scale),
            Mathf.CeilToInt(outputHeight * scale),
            ScalableBufferManager.widthScaleFactor,
            ScalableBufferManager.heightScaleFactor
        );
    }

    private static UrpUpscalingState Unavailable(float fsrSharpness) =>
        new(
            false,
            UrpUpscalingFilter.Auto,
            1f,
            Mathf.Clamp01(fsrSharpness),
            Screen.width,
            Screen.height,
            Screen.width,
            Screen.height,
            ScalableBufferManager.widthScaleFactor,
            ScalableBufferManager.heightScaleFactor
        );

    private readonly record struct AssetSnapshot(
        float RenderScale,
        UrpUpscalingFilter Filter,
        bool FsrOverrideSharpness,
        float FsrSharpness,
        float DynamicWidthScale,
        float DynamicHeightScale
    )
    {
        internal static AssetSnapshot Capture(UrpAssetSurface asset) =>
            new(
                asset.RenderScale,
                asset.Filter,
                asset.FsrOverrideSharpness,
                asset.FsrSharpness,
                ScalableBufferManager.widthScaleFactor,
                ScalableBufferManager.heightScaleFactor
            );

        internal void Restore(UrpAssetSurface asset)
        {
            asset.RenderScale = RenderScale;
            asset.Filter = Filter;
            asset.FsrOverrideSharpness = FsrOverrideSharpness;
            asset.FsrSharpness = FsrSharpness;
        }
    }

    private sealed class UrpAssetSurface
    {
        private readonly UnityEngine.Object _asset;
        private readonly System.Reflection.PropertyInfo _renderScale;
        private readonly System.Reflection.PropertyInfo _filter;
        private readonly System.Reflection.PropertyInfo _fsrOverrideSharpness;
        private readonly System.Reflection.PropertyInfo _fsrSharpness;

        private UrpAssetSurface(
            UnityEngine.Object asset,
            System.Reflection.PropertyInfo renderScale,
            System.Reflection.PropertyInfo filter,
            System.Reflection.PropertyInfo fsrOverrideSharpness,
            System.Reflection.PropertyInfo fsrSharpness
        )
        {
            _asset = asset;
            _renderScale = renderScale;
            _filter = filter;
            _fsrOverrideSharpness = fsrOverrideSharpness;
            _fsrSharpness = fsrSharpness;
        }

        internal int InstanceId => _asset.GetInstanceID();
        internal bool IsAlive => _asset != null;

        internal float RenderScale
        {
            get => (float)_renderScale.GetValue(_asset);
            set => _renderScale.SetValue(_asset, value);
        }

        internal UrpUpscalingFilter Filter
        {
            get => (UrpUpscalingFilter)Convert.ToInt32(_filter.GetValue(_asset));
            set => _filter.SetValue(_asset, Enum.ToObject(_filter.PropertyType, (int)value));
        }

        internal bool FsrOverrideSharpness
        {
            get => (bool)_fsrOverrideSharpness.GetValue(_asset);
            set => _fsrOverrideSharpness.SetValue(_asset, value);
        }

        internal float FsrSharpness
        {
            get => (float)_fsrSharpness.GetValue(_asset);
            set => _fsrSharpness.SetValue(_asset, value);
        }

        internal static UrpAssetSurface? TryCreate(RenderPipelineAsset? asset)
        {
            if (asset == null || asset.GetType().FullName != UniversalPipelineAssetTypeName)
                return null;

            var type = asset.GetType();
            var renderScale = WritableProperty(type, "renderScale");
            var filter = WritableProperty(type, "upscalingFilter");
            var fsrOverrideSharpness = WritableProperty(type, "fsrOverrideSharpness");
            var fsrSharpness = WritableProperty(type, "fsrSharpness");
            return
                renderScale == null
                || filter == null
                || fsrOverrideSharpness == null
                || fsrSharpness == null
                ? null
                : new UrpAssetSurface(
                    asset,
                    renderScale,
                    filter,
                    fsrOverrideSharpness,
                    fsrSharpness
                );
        }

        private static System.Reflection.PropertyInfo? WritableProperty(Type type, string name)
        {
            var property = type.GetProperty(name);
            return property is { CanRead: true, CanWrite: true } ? property : null;
        }
    }
}
