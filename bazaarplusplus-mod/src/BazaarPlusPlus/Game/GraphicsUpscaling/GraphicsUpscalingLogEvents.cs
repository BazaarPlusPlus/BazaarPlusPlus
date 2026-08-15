#nullable enable
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Game.GraphicsUpscaling;

[BppLogEventSource]
internal static class GraphicsUpscalingLogEvents
{
    internal static readonly BppLogFieldDefinition AppliedMode = PublicLow(0, "mode");
    internal static readonly BppLogFieldDefinition AppliedEffectiveFilter = PublicLow(
        1,
        "effective_filter"
    );
    internal static readonly BppLogFieldDefinition AppliedRenderScale = PublicLow(
        2,
        "render_scale"
    );
    internal static readonly BppLogFieldDefinition AppliedRenderPixelRatio = PublicLow(
        3,
        "render_pixel_ratio"
    );
    internal static readonly BppLogFieldDefinition AppliedFsrSharpness = PublicLow(
        4,
        "fsr_sharpness"
    );
    internal static readonly BppLogFieldDefinition AppliedOutputResolution = PublicHigh(
        5,
        "output_resolution"
    );
    internal static readonly BppLogFieldDefinition AppliedInternalResolution = PublicHigh(
        6,
        "internal_resolution"
    );
    internal static readonly BppLogFieldDefinition AppliedDynamicBufferScale = PublicLow(
        7,
        "dynamic_buffer_scale"
    );
    internal static readonly BppLogEventDefinition Applied = new(
        BppLogFeatureScope.GraphicsUpscaling,
        "graphics_upscaling.state.applied",
        [
            AppliedMode,
            AppliedEffectiveFilter,
            AppliedRenderScale,
            AppliedRenderPixelRatio,
            AppliedFsrSharpness,
            AppliedOutputResolution,
            AppliedInternalResolution,
            AppliedDynamicBufferScale,
        ]
    );

    internal static readonly BppLogFieldDefinition UnavailableMode = PublicLow(0, "mode");
    internal static readonly BppLogEventDefinition Unavailable = new(
        BppLogFeatureScope.GraphicsUpscaling,
        "graphics_upscaling.runtime.unavailable",
        [UnavailableMode],
        new BppLogStormPolicy([UnavailableMode])
    );

    private static BppLogFieldDefinition PublicLow(int order, string name) =>
        new(order, name, BppLogCorrelationPolicy.None, BppLogCardinality.Low);

    private static BppLogFieldDefinition PublicHigh(int order, string name) =>
        new(order, name, BppLogCorrelationPolicy.None, BppLogCardinality.High);
}
