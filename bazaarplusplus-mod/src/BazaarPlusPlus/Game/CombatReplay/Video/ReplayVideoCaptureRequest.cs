#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoCaptureRequest
{
    public string VideoId { get; init; } = string.Empty;

    public string BattleId { get; init; } = string.Empty;

    public CombatReplayPlaybackSource Source { get; init; }

    public string FfmpegExecutable { get; init; } = string.Empty;

    public string OutputFilePath { get; init; } = string.Empty;

    public string FinalOutputFilePath { get; init; } = string.Empty;

    public string OutputDirectoryPath { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    public int Fps { get; init; }

    public FfmpegVideoEncoderProfile EncoderProfile { get; init; } =
        FfmpegVideoEncoderProfile.Libx264();

    public ReplayVideoBufferPlan BufferPlan { get; init; } = ReplayVideoBufferPlan.Create(2, 2);
}
