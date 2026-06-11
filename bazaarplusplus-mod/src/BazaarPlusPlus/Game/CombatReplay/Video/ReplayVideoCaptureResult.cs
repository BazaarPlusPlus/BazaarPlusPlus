#nullable enable
using System;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoCaptureResult
{
    public string VideoId { get; init; } = string.Empty;

    public string BattleId { get; init; } = string.Empty;

    public CombatReplayPlaybackSource Source { get; init; }

    public string OutputFilePath { get; init; } = string.Empty;

    public int Width { get; init; }

    public int Height { get; init; }

    public int Fps { get; init; }

    public string Codec { get; init; } = "libx264";

    public int Crf { get; init; }

    public string Preset { get; init; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; init; }

    public long DurationMs { get; init; }

    public int CapturedFrames { get; init; }

    public int DroppedFrames { get; init; }

    public long FileSizeBytes { get; init; }

    public ReplayVideoCaptureStatus Status { get; init; }

    public string? Error { get; init; }
}

internal enum ReplayVideoCaptureStatus
{
    Recording,
    Completed,
    Failed,
}
