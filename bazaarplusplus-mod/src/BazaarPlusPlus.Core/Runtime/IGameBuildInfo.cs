#nullable enable
namespace BazaarPlusPlus.Core.Runtime;

public enum GameBuildChannel
{
    Online,

    // Persisted isolation category for PTR and staging clients.
    Ptr,

    // Detection failed (version string unreadable). Policy gates must treat Unknown
    // like Online so a detection failure can never change online behavior.
    Unknown,
}

public interface IGameBuildInfo
{
    string RawVersion { get; }
    GameBuildChannel Channel { get; }
}
