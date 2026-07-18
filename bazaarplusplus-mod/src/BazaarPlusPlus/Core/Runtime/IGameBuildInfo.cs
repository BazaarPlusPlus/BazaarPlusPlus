#nullable enable
namespace BazaarPlusPlus.Core.Runtime;

internal enum GameBuildChannel
{
    Online,
    Ptr,

    // Detection failed (version string unreadable). Policy gates must treat Unknown
    // like Online so a detection failure can never change online behavior.
    Unknown,
}

internal interface IGameBuildInfo
{
    string RawVersion { get; }
    GameBuildChannel Channel { get; }
}
