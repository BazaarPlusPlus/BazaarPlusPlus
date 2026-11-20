#nullable enable
namespace BazaarPlusPlus.Game.CombatReplay.Video;

internal enum ReplayVideoBackend
{
    MacNative,
    WindowsNative,
    Unsupported,
}

internal static class ReplayVideoBackendPolicy
{
    internal static ReplayVideoBackend Current =>
        ForPlatform(ReplayVideoEncoderProfile.DetectPlatform());

    internal static ReplayVideoBackend ForPlatform(VideoEncoderPlatform platform) =>
        platform switch
        {
            VideoEncoderPlatform.MacOS => ReplayVideoBackend.MacNative,
            VideoEncoderPlatform.Windows => ReplayVideoBackend.WindowsNative,
            _ => ReplayVideoBackend.Unsupported,
        };
}
