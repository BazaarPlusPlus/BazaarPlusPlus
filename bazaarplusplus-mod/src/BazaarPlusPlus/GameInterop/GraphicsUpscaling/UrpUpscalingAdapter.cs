#nullable enable
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarPlusPlus.GameInterop.GraphicsUpscaling;

internal sealed class UrpUpscalingAdapter : IDisposable
{
    private const string UniversalPipelineAssetTypeName =
        "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";
    private UrpAssetSurface? _modifiedAsset;
    private AssetSnapshot _original;
    private RenderPipelineAsset? _surfaceSource;
    private UrpAssetSurface? _surface;

    // Runs on every controller refresh: reuse the reflected surface while the same pipeline
    // asset stays current, and write a property only when the game changed it back.
    internal UrpUpscalingState Apply(bool enabled, float renderScale, float fsrSharpness)
    {
        var currentAsset = ResolveSurface(GraphicsSettings.currentRenderPipeline);
        if (currentAsset == null)
        {
            Restore();
            return Unavailable(fsrSharpness);
        }

        if (!enabled)
        {
            // Restore never changes which asset is current, so the surface stays valid.
            Restore();
            return Observe(currentAsset);
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

        var targetScale = Mathf.Clamp(renderScale, 0.1f, 1f);
        if (currentAsset.RenderScale != targetScale)
            currentAsset.RenderScale = targetScale;
        if (currentAsset.Filter != UrpUpscalingFilter.Fsr)
            currentAsset.SetFsrFilter();
        if (!currentAsset.FsrOverrideSharpness)
            currentAsset.FsrOverrideSharpness = true;
        var targetSharpness = Mathf.Clamp01(fsrSharpness);
        if (currentAsset.FsrSharpness != targetSharpness)
            currentAsset.FsrSharpness = targetSharpness;

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

    // A quality preset switch replaces GraphicsSettings.currentRenderPipeline; until then the
    // surface reflected for the current asset is reused.
    private UrpAssetSurface? ResolveSurface(RenderPipelineAsset? asset)
    {
        if (asset != null && ReferenceEquals(asset, _surfaceSource))
            return _surface;

        _surfaceSource = asset;
        _surface = UrpAssetSurface.TryCreate(asset);
        return _surface;
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
        private readonly PropertyInfo _filter;
        private readonly object _fsrFilterValue;
        private readonly Func<float> _getRenderScale;
        private readonly Action<float> _setRenderScale;
        private readonly Func<bool> _getFsrOverrideSharpness;
        private readonly Action<bool> _setFsrOverrideSharpness;
        private readonly Func<float> _getFsrSharpness;
        private readonly Action<float> _setFsrSharpness;

        private UrpAssetSurface(
            UnityEngine.Object asset,
            PropertyInfo renderScale,
            PropertyInfo filter,
            PropertyInfo fsrOverrideSharpness,
            PropertyInfo fsrSharpness
        )
        {
            _asset = asset;
            _filter = filter;
            _fsrFilterValue = Enum.ToObject(filter.PropertyType, (int)UrpUpscalingFilter.Fsr);
            // Delegates bound to this asset read and write the float and bool properties
            // without boxing; the enum-typed filter keeps reflection.
            _getRenderScale = Getter<float>(asset, renderScale);
            _setRenderScale = Setter<float>(asset, renderScale);
            _getFsrOverrideSharpness = Getter<bool>(asset, fsrOverrideSharpness);
            _setFsrOverrideSharpness = Setter<bool>(asset, fsrOverrideSharpness);
            _getFsrSharpness = Getter<float>(asset, fsrSharpness);
            _setFsrSharpness = Setter<float>(asset, fsrSharpness);
        }

        internal int InstanceId => _asset.GetInstanceID();
        internal bool IsAlive => _asset != null;

        internal float RenderScale
        {
            get => _getRenderScale();
            set => _setRenderScale(value);
        }

        internal UrpUpscalingFilter Filter
        {
            get =>
                (UrpUpscalingFilter)
                    Convert.ToInt32(_filter.GetValue(_asset), CultureInfo.InvariantCulture);
            set => _filter.SetValue(_asset, Enum.ToObject(_filter.PropertyType, (int)value));
        }

        internal bool FsrOverrideSharpness
        {
            get => _getFsrOverrideSharpness();
            set => _setFsrOverrideSharpness(value);
        }

        internal float FsrSharpness
        {
            get => _getFsrSharpness();
            set => _setFsrSharpness(value);
        }

        internal void SetFsrFilter() => _filter.SetValue(_asset, _fsrFilterValue);

        internal static UrpAssetSurface? TryCreate(RenderPipelineAsset? asset)
        {
            if (asset == null || asset.GetType().FullName != UniversalPipelineAssetTypeName)
                return null;

            var type = asset.GetType();
            var renderScale = WritableProperty(type, "renderScale", typeof(float));
            var filter = WritableProperty(type, "upscalingFilter", null);
            var fsrOverrideSharpness = WritableProperty(type, "fsrOverrideSharpness", typeof(bool));
            var fsrSharpness = WritableProperty(type, "fsrSharpness", typeof(float));
            return
                renderScale == null
                || filter is not { PropertyType.IsEnum: true }
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

        private static PropertyInfo? WritableProperty(Type type, string name, Type? propertyType)
        {
            var property = type.GetProperty(name);
            return
                property is { CanRead: true, CanWrite: true }
                && (propertyType == null || property.PropertyType == propertyType)
                ? property
                : null;
        }

        private static Func<T> Getter<T>(object asset, PropertyInfo property) =>
            (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), asset, property.GetGetMethod(true)!);

        private static Action<T> Setter<T>(object asset, PropertyInfo property) =>
            (Action<T>)
                Delegate.CreateDelegate(typeof(Action<T>), asset, property.GetSetMethod(true)!);
    }
}
