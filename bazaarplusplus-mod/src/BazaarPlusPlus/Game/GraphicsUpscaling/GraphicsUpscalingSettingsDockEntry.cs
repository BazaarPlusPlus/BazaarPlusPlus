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
            config => config.GraphicsUpscalingModeConfig?.Value ?? GraphicsUpscalingMode.Native,
            (config, mode) =>
            {
                var entry = config.GraphicsUpscalingModeConfig;
                if (entry != null)
                    entry.Value = mode;
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

    private static string ResolveStatus(GraphicsUpscalingMode mode, string languageCode)
    {
        if (LanguageCodeMatcher.IsChinese(languageCode))
        {
            return mode switch
            {
                GraphicsUpscalingMode.Native => "原生",
                GraphicsUpscalingMode.FsrUltraQuality => "超高质量 · 77%",
                GraphicsUpscalingMode.FsrQuality => "质量 · 67%",
                GraphicsUpscalingMode.FsrBalanced => "均衡 · 59%",
                GraphicsUpscalingMode.FsrPerformance => "性能 · 50%",
                _ => "原生",
            };
        }

        return mode switch
        {
            GraphicsUpscalingMode.Native => "NATIVE",
            GraphicsUpscalingMode.FsrUltraQuality => "ULTRA QUALITY · 77%",
            GraphicsUpscalingMode.FsrQuality => "QUALITY · 67%",
            GraphicsUpscalingMode.FsrBalanced => "BALANCED · 59%",
            GraphicsUpscalingMode.FsrPerformance => "PERFORMANCE · 50%",
            _ => "NATIVE",
        };
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
            config =>
                config.GraphicsUpscalingSharpnessConfig?.Value ?? BppConfig.DefaultFsrSharpness,
            (config, sharpness) =>
            {
                var entry = config.GraphicsUpscalingSharpnessConfig;
                if (entry != null)
                    entry.Value = sharpness;
            },
            sharpness => !Approximately(sharpness, BppConfig.DefaultFsrSharpness),
            ResolveStatus
        );

    private static string ResolveStatus(float sharpness, string languageCode)
    {
        var percentage = $"{MathF.Round(sharpness * 100f)}%";
        if (Approximately(sharpness, BppConfig.DefaultFsrSharpness))
        {
            return LanguageCodeMatcher.IsChinese(languageCode)
                ? $"推荐 · {percentage}"
                : $"RECOMMENDED · {percentage}";
        }

        return percentage;
    }

    private static bool Approximately(float left, float right) => MathF.Abs(left - right) < 0.001f;
}
