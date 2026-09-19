#nullable enable
using System.Runtime.InteropServices;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum VideoEncoderPlatform
{
    MacOS,
    Windows,
    Other,
}

internal sealed class ReplayVideoEncoderProfile
{
    private ReplayVideoEncoderProfile(string codec, string pixelFormat, int targetBitrateKbps)
    {
        Codec = codec;
        PixelFormat = pixelFormat;
        TargetBitrateKbps = targetBitrateKbps;
    }

    internal string Codec { get; }
    internal string PixelFormat { get; }
    internal int? Crf => null;
    internal string Preset => "realtime-average-bitrate";
    internal int TargetBitrateKbps { get; }
    internal string RateControlSummary => $"avg_bitrate={TargetBitrateKbps}k";

    internal static ReplayVideoEncoderProfile NativeVideoToolbox(int width, int height, int fps) =>
        Create("h264_videotoolbox", width, height, fps);

    internal static ReplayVideoEncoderProfile NativeMediaFoundation(
        int width,
        int height,
        int fps
    ) => Create("h264_media_foundation", width, height, fps);

    private static ReplayVideoEncoderProfile Create(string codec, int width, int height, int fps)
    {
        var targetKbps = CalculateTargetBitrateKbps(width, height, fps);
        return new(codec, "nv12", targetKbps);
    }

    internal static VideoEncoderPlatform DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return VideoEncoderPlatform.MacOS;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return VideoEncoderPlatform.Windows;
        return VideoEncoderPlatform.Other;
    }

    internal static int CalculateTargetBitrateKbps(int width, int height, int fps)
    {
        if (width <= 0 || height <= 0 || fps <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Video dimensions and FPS must be positive."
            );

        var bitsPerSecond = (double)width * height * fps * 0.15d;
        var kbps = (int)Math.Round(bitsPerSecond / 1000d, MidpointRounding.AwayFromZero);
        return Math.Max(6000, Math.Min(24000, kbps));
    }
}
