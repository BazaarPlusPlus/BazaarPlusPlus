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

internal static class CombatReplayRecordingGate
{
    // Read Unity-owned dimensions and FPS on the main thread.
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
