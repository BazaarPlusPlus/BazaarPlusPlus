#nullable enable
using System;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoCaptureRequest
{
    public string VideoId { get; init; } = Guid.NewGuid().ToString("N");

    public string BattleId { get; init; } = string.Empty;

    public CombatReplayPlaybackSource Source { get; init; }

    public string FfmpegExecutable { get; init; } = string.Empty;

    public string OutputFilePath { get; init; } = string.Empty;

    public string OutputDirectoryPath { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    public int Fps { get; init; }

    public int Crf { get; init; }

    public string Preset { get; init; } = "veryfast";

    public int MaxQueuedFrames { get; init; }
}
