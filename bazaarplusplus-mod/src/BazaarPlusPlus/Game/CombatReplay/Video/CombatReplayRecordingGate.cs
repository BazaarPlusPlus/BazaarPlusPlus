#nullable enable
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum CombatReplayRecordingBlocker
{
    None,
    NoAsyncGpuReadback,
    FfmpegUnavailable,
    VideoDirectoryUnset,
}

internal readonly struct CombatReplayRecordingGateResult
{
    public CombatReplayRecordingGateResult(
        CombatReplayRecordingBlocker blocker,
        string? ffmpegExecutable,
        string? videoDirectoryPath
    )
    {
        Blocker = blocker;
        FfmpegExecutable = ffmpegExecutable;
        VideoDirectoryPath = videoDirectoryPath;
    }

    public CombatReplayRecordingBlocker Blocker { get; }
    public string? FfmpegExecutable { get; }
    public string? VideoDirectoryPath { get; }
    public bool CanRecord => Blocker == CombatReplayRecordingBlocker.None;
}

/// <summary>
/// The single source of truth for "can a replay video recording actually start": async GPU
/// readback support, a resolvable FFmpeg, and a configured video directory. The recorder enforces
/// it at capture time (and bails silently when blocked), so every pre-check that promises a
/// recording must evaluate this same gate — a drifted copy would promise recordings that silently
/// never happen.
/// <see cref="FfmpegLocator.Resolve"/> probes (~2s) on its first process-wide call; callers on
/// the main thread must prewarm it off-thread first.
/// </summary>
internal static class CombatReplayRecordingGate
{
    public static CombatReplayRecordingGateResult Evaluate(
        string? pluginsDirectoryPath,
        string? videoDirectoryPath
    )
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
            return new(CombatReplayRecordingBlocker.NoAsyncGpuReadback, null, null);

        var ffmpegExecutable = FfmpegLocator.Resolve(pluginsDirectoryPath);
        if (string.IsNullOrEmpty(ffmpegExecutable))
            return new(CombatReplayRecordingBlocker.FfmpegUnavailable, null, null);

        if (string.IsNullOrWhiteSpace(videoDirectoryPath))
            return new(CombatReplayRecordingBlocker.VideoDirectoryUnset, ffmpegExecutable, null);

        return new(CombatReplayRecordingBlocker.None, ffmpegExecutable, videoDirectoryPath);
    }
}
