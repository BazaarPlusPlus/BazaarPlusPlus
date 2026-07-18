#nullable enable
using System.Runtime.InteropServices;
using System.Text;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum VideoEncoderPlatform
{
    MacOS,
    Windows,
    Other,
}

internal sealed class FfmpegVideoEncoderProfile
{
    private FfmpegVideoEncoderProfile(
        string codec,
        string pixelFormat,
        bool hardwareAccelerated,
        int? crf,
        string? preset,
        int? targetBitrateKbps,
        int? maxBitrateKbps,
        int? bufferSizeKbps,
        string extraArguments
    )
    {
        Codec = codec;
        PixelFormat = pixelFormat;
        HardwareAccelerated = hardwareAccelerated;
        Crf = crf;
        Preset = preset;
        TargetBitrateKbps = targetBitrateKbps;
        MaxBitrateKbps = maxBitrateKbps;
        BufferSizeKbps = bufferSizeKbps;
        ExtraArguments = extraArguments;
    }

    internal string Codec { get; }
    internal string PixelFormat { get; }
    internal bool HardwareAccelerated { get; }
    internal int? Crf { get; }
    internal string? Preset { get; }
    internal int? TargetBitrateKbps { get; }
    internal int? MaxBitrateKbps { get; }
    internal int? BufferSizeKbps { get; }
    internal string ExtraArguments { get; }

    internal string RateControlSummary =>
        Crf.HasValue
            ? $"crf={Crf.Value},preset={Preset}"
            : $"vbr={TargetBitrateKbps}k,maxrate={MaxBitrateKbps}k,bufsize={BufferSizeKbps}k";

    internal static FfmpegVideoEncoderProfile Libx264() =>
        new(
            "libx264",
            "yuv420p",
            hardwareAccelerated: false,
            ReplayVideoCaptureDefaults.Crf,
            ReplayVideoCaptureDefaults.Preset,
            targetBitrateKbps: null,
            maxBitrateKbps: null,
            bufferSizeKbps: null,
            extraArguments: string.Empty
        );

    internal static IReadOnlyList<FfmpegVideoEncoderProfile> Candidates(
        VideoEncoderPlatform platform,
        int width,
        int height,
        int fps
    )
    {
        var targetKbps = CalculateTargetBitrateKbps(width, height, fps);
        var maxKbps = (int)Math.Ceiling(targetKbps * 1.25d);
        var bufferKbps = checked(targetKbps * 2);

        FfmpegVideoEncoderProfile Hardware(
            string codec,
            string pixelFormat,
            string preset,
            string extraArguments = ""
        ) =>
            new(
                codec,
                pixelFormat,
                hardwareAccelerated: true,
                crf: null,
                preset,
                targetKbps,
                maxKbps,
                bufferKbps,
                extraArguments
            );

        return platform switch
        {
            VideoEncoderPlatform.MacOS =>
            [
                Hardware("h264_videotoolbox", "yuv420p", "realtime-vbr", "-realtime 1"),
            ],
            VideoEncoderPlatform.Windows =>
            [
                Hardware("h264_nvenc", "yuv420p", "vbr"),
                Hardware("h264_qsv", "nv12", "vbr"),
                Hardware("h264_amf", "nv12", "vbr"),
            ],
            _ => Array.Empty<FfmpegVideoEncoderProfile>(),
        };
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

internal static class FfmpegVideoEncoderArguments
{
    internal static string Build(
        FfmpegVideoEncoderProfile profile,
        int width,
        int height,
        int fps,
        string outputFilePath,
        int? frameLimit = null
    )
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        var sb = new StringBuilder();
        sb.Append("-hide_banner -loglevel warning -nostdin -y ");
        sb.Append("-f rawvideo -pixel_format rgba ");
        sb.Append($"-video_size {width}x{height} ");
        sb.Append($"-framerate {fps} ");
        sb.Append("-i pipe:0 ");
        sb.Append($"-c:v {profile.Codec} -pix_fmt {profile.PixelFormat} ");

        if (profile.Crf.HasValue)
        {
            sb.Append($"-preset {profile.Preset} ");
            sb.Append($"-crf {profile.Crf.Value} ");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(profile.ExtraArguments))
                sb.Append(profile.ExtraArguments).Append(' ');
            sb.Append($"-b:v {profile.TargetBitrateKbps}k ");
            sb.Append($"-maxrate {profile.MaxBitrateKbps}k ");
            sb.Append($"-bufsize {profile.BufferSizeKbps}k ");
        }

        if (frameLimit.HasValue)
            sb.Append($"-frames:v {frameLimit.Value} ");

        sb.Append("-movflags +faststart ");
        sb.Append(VideoProcessHelpers.QuoteArg(outputFilePath));
        return sb.ToString();
    }
}
