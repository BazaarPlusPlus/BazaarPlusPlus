#nullable enable
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.GraphicsUpscaling;

internal readonly record struct FsrUpscalingProfile(bool Enabled, float RenderScale);

internal static class FsrUpscalingProfiles
{
    internal static FsrUpscalingProfile Resolve(GraphicsUpscalingMode mode) =>
        mode switch
        {
            GraphicsUpscalingMode.FsrUltraQuality => new(true, 0.77f),
            GraphicsUpscalingMode.FsrQuality => new(true, 0.67f),
            GraphicsUpscalingMode.FsrBalanced => new(true, 0.59f),
            GraphicsUpscalingMode.FsrPerformance => new(true, 0.5f),
            _ => new(false, 1f),
        };
}
