#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum CombatReplayRecordingBlocker
{
    None,
    UnsupportedPlatform,
    NativeRecorderUnavailable,
    VideoDirectoryUnset,
}

internal readonly struct CombatReplayRecordingGateResult
{
    public CombatReplayRecordingGateResult(
        CombatReplayRecordingBlocker blocker,
        string? videoDirectoryPath
    )
    {
        Blocker = blocker;
        VideoDirectoryPath = videoDirectoryPath;
    }

    public CombatReplayRecordingBlocker Blocker { get; }
    public string? VideoDirectoryPath { get; }
    public bool CanRecord => Blocker == CombatReplayRecordingBlocker.None;
}

/// <summary>
/// The single source of truth for whether native replay recording can start.
/// </summary>
internal static class CombatReplayRecordingGate
{
    public static CombatReplayRecordingGateResult Evaluate(
        string? pluginsDirectoryPath,
        string? videoDirectoryPath
    )
    {
        _ = pluginsDirectoryPath;
        var backend = ReplayVideoBackendPolicy.Current;
        var available = backend switch
        {
            ReplayVideoBackend.MacNative => MacMetalVideoEncoder.TryGetAvailability(out _),
            ReplayVideoBackend.WindowsNative =>
                WindowsMediaFoundationVideoEncoder.TryGetAvailability(out _),
            _ => false,
        };

        if (backend == ReplayVideoBackend.Unsupported)
            return new(CombatReplayRecordingBlocker.UnsupportedPlatform, null);
        if (!available)
            return new(CombatReplayRecordingBlocker.NativeRecorderUnavailable, null);
        return string.IsNullOrWhiteSpace(videoDirectoryPath)
            ? new(CombatReplayRecordingBlocker.VideoDirectoryUnset, null)
            : new(CombatReplayRecordingBlocker.None, videoDirectoryPath);
    }
}
