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
    /// <summary>
    /// Captures the Unity-owned dimensions and FPS and loads the native plugin. Call it on the UI
    /// thread, so a later <see cref="Evaluate"/> finds the backend probe warm.
    /// </summary>
    public static void Prewarm()
    {
        ReplayVideoCaptureSettingsCache.TryCaptureCurrent(out _);
        NativeRecorderAvailable(ReplayVideoBackendPolicy.Current);
    }

    public static CombatReplayRecordingGateResult Evaluate(string? videoDirectoryPath)
    {
        var backend = ReplayVideoBackendPolicy.Current;
        var available = NativeRecorderAvailable(backend);

        if (backend == ReplayVideoBackend.Unsupported)
            return new(CombatReplayRecordingBlocker.UnsupportedPlatform, null);
        if (!available)
            return new(CombatReplayRecordingBlocker.NativeRecorderUnavailable, null);
        return string.IsNullOrWhiteSpace(videoDirectoryPath)
            ? new(CombatReplayRecordingBlocker.VideoDirectoryUnset, null)
            : new(CombatReplayRecordingBlocker.None, videoDirectoryPath);
    }

    private static bool NativeRecorderAvailable(ReplayVideoBackend backend) =>
        backend switch
        {
            ReplayVideoBackend.MacNative => MacMetalVideoEncoder.TryGetAvailability(out _),
            ReplayVideoBackend.WindowsNative =>
                WindowsMediaFoundationVideoEncoder.TryGetAvailability(out _),
            _ => false,
        };
}
