#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal sealed class ReplayVideoCaptureResult
{
    public string VideoId { get; init; } = string.Empty;

    public string OutputFilePath { get; init; } = string.Empty;

    public DateTimeOffset? EndedAtUtc { get; init; }

    public long DurationMs { get; init; }

    public int CapturedFrames { get; init; }

    public int DroppedFrames { get; init; }

    public long FileSizeBytes { get; init; }

    public ReplayVideoCaptureStatus Status { get; init; }

    public string? Error { get; init; }

    public ReplayVideoRecordingReasonCode ReasonCode { get; init; }

    public int? ExitCode { get; init; }

    public string? StderrTail { get; init; }

    public Exception? Exception { get; init; }

    public bool Degraded { get; init; }
}

internal enum ReplayVideoCaptureStatus
{
    Completed,
    Failed,
}
