#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoCaptureRequest
{
    public string VideoId { get; init; } = string.Empty;

    public string BattleId { get; init; } = string.Empty;

    public CombatReplayPlaybackSource Source { get; init; }

    public string OutputFilePath { get; init; } = string.Empty;

    public string FinalOutputFilePath { get; init; } = string.Empty;

    public string OutputDirectoryPath { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    public int Fps { get; init; }

    // No default: the sole construction site always supplies both, and the previous placeholder
    // initializers ran a platform probe (which throws on unsupported platforms) only to be
    // overwritten by the object initializer.
    public ReplayVideoEncoderProfile EncoderProfile { get; init; } = null!;

    public ReplayVideoBufferPlan BufferPlan { get; init; } = null!;
}
