#nullable enable
namespace BazaarPlusPlus.Core.Runtime;

public enum GameBuildChannel
{
    Online,

    // PTR and staging share one persisted isolation category.
    Ptr,

    // Treat Unknown like Online so a failed probe cannot change production behavior.
    Unknown,
}

public interface IGameBuildInfo
{
    string RawVersion { get; }
    GameBuildChannel Channel { get; }
}
