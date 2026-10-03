#nullable enable
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Localization;

namespace BazaarPlusPlus.Game.GraphicsUpscaling;

internal static class GraphicsUpscalingSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new(
        "FSR 1 Upscaling",
        "FSR 1 超分",
        "FSR 1 超解析"
    );

    internal static CyclingSettingsDockEntry<GraphicsUpscalingMode> Create() =>
        new(
            BppSettingsDockOrder.GraphicsUpscaling,
            "GraphicsUpscaling",
            languageCode => Labels.Resolve(languageCode, L.CurrentMode),
            new[]
            {
                GraphicsUpscalingMode.Native,
                GraphicsUpscalingMode.FsrUltraQuality,
                GraphicsUpscalingMode.FsrQuality,
                GraphicsUpscalingMode.FsrBalanced,
                GraphicsUpscalingMode.FsrPerformance,
            },
            config => config.GraphicsUpscalingModeConfig.Value,
            (config, mode) =>
            {
                config.GraphicsUpscalingModeConfig.Value = mode;
            },
            mode => mode != GraphicsUpscalingMode.Native,
            ResolveStatus
        );

    internal static void RegisterAll(SettingsDockEntryRegistry registry)
    {
        if (registry == null)
            throw new ArgumentNullException(nameof(registry));

        registry.Register(Create());
        registry.Register(GraphicsUpscalingSharpnessSettingsDockEntry.Create());
    }

    private static readonly LocalizedTextSet NativeStatus = new("NATIVE", "原生");
    private static readonly LocalizedTextSet UltraQualityStatus = new(
        "ULTRA QUALITY · 77%",
        "超高质量 · 77%"
    );
    private static readonly LocalizedTextSet QualityStatus = new("QUALITY · 67%", "质量 · 67%");
    private static readonly LocalizedTextSet BalancedStatus = new("BALANCED · 59%", "均衡 · 59%");
    private static readonly LocalizedTextSet PerformanceStatus = new(
        "PERFORMANCE · 50%",
        "性能 · 50%"
    );

    private static string ResolveStatus(GraphicsUpscalingMode mode, string languageCode)
    {
        var status = mode switch
        {
            GraphicsUpscalingMode.FsrUltraQuality => UltraQualityStatus,
            GraphicsUpscalingMode.FsrQuality => QualityStatus,
            GraphicsUpscalingMode.FsrBalanced => BalancedStatus,
            GraphicsUpscalingMode.FsrPerformance => PerformanceStatus,
            _ => NativeStatus,
        };
        return status.Resolve(languageCode, L.CurrentMode);
    }
}

internal static class GraphicsUpscalingSharpnessSettingsDockEntry
{
    private static readonly LocalizedTextSet Labels = new(
        "FSR Sharpness",
        "FSR 锐化强度",
        "FSR 銳化強度"
    );

    internal static CyclingSettingsDockEntry<float> Create() =>
        new(
            BppSettingsDockOrder.GraphicsUpscalingSharpness,
            "GraphicsUpscalingSharpness",
            languageCode => Labels.Resolve(languageCode, L.CurrentMode),
            new[] { 0.6f, 0.75f, BppConfig.DefaultFsrSharpness, 1f },
            config => config.GraphicsUpscalingSharpnessConfig.Value,
            (config, sharpness) =>
            {
                config.GraphicsUpscalingSharpnessConfig.Value = sharpness;
            },
            sharpness => !Approximately(sharpness, BppConfig.DefaultFsrSharpness),
            ResolveStatus
        );

    private static string ResolveStatus(float sharpness, string languageCode)
    {
        var percentage = $"{MathF.Round(sharpness * 100f)}%";
        if (Approximately(sharpness, BppConfig.DefaultFsrSharpness))
        {
            return new LocalizedTextSet(
                $"RECOMMENDED · {percentage}",
                $"推荐 · {percentage}"
            ).Resolve(languageCode, L.CurrentMode);
        }

        return percentage;
    }

    private static bool Approximately(float left, float right) => MathF.Abs(left - right) < 0.001f;
}
