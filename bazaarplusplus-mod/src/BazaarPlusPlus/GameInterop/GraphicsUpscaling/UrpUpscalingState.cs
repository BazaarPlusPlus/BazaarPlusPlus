#nullable enable
namespace BazaarPlusPlus.GameInterop.GraphicsUpscaling;

internal enum UrpUpscalingFilter
{
    Auto,
    Linear,
    Point,
    Fsr,
}

internal readonly record struct UrpUpscalingState(
    bool AssetAvailable,
    UrpUpscalingFilter EffectiveFilter,
    float RenderScale,
    float FsrSharpness,
    int OutputWidth,
    int OutputHeight,
    int InternalWidth,
    int InternalHeight,
    float DynamicWidthScale,
    float DynamicHeightScale
);
