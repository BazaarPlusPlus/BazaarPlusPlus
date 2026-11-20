#nullable enable
namespace BazaarPlusPlus.Core.Runtime;

public sealed class GameBuildInfo : IGameBuildInfo
{
    public GameBuildInfo(string rawVersion, GameBuildChannel channel, string? detectionWarning)
    {
        RawVersion = rawVersion;
        Channel = channel;
        DetectionWarning = detectionWarning;
    }

    public string RawVersion { get; }
    public GameBuildChannel Channel { get; }

    // Resolution precedes logging setup, so the caller reports this warning at startup.
    public string? DetectionWarning { get; }
}

public static class GameBuildInfoResolver
{
    // Only the PTR fork declares TheBazaar.Config.ServerOption. On disagreement, prefer PTR
    // because sending PTR runs would contaminate the production dataset.
    public static GameBuildInfo Resolve(string rawVersion, bool? hasServerOption)
    {
        // Staging lacks Config.ServerOption. Reuse the persisted Ptr isolation category
        // so its recorded runs stay excluded from uploads after switching back to Online.
        if (rawVersion.IndexOf("-staging", StringComparison.OrdinalIgnoreCase) >= 0)
            return new GameBuildInfo(rawVersion, GameBuildChannel.Ptr, null);

        var byVersion = GameBuildChannel.Unknown;
        if (rawVersion.Length > 0)
        {
            byVersion =
                rawVersion.IndexOf("-ptr", StringComparison.OrdinalIgnoreCase) >= 0
                    ? GameBuildChannel.Ptr
                    : GameBuildChannel.Online;
        }

        var byProbe = hasServerOption switch
        {
            true => GameBuildChannel.Ptr,
            false => GameBuildChannel.Online,
            null => GameBuildChannel.Unknown,
        };

        if (byVersion == GameBuildChannel.Unknown)
        {
            return new GameBuildInfo(
                rawVersion,
                byProbe,
                $"Game version string unreadable; channel from Config probe alone: {byProbe}."
            );
        }

        if (byProbe != GameBuildChannel.Unknown && byProbe != byVersion)
        {
            return new GameBuildInfo(
                rawVersion,
                GameBuildChannel.Ptr,
                $"Build channel signals disagree (version '{rawVersion}' → {byVersion}, Config.ServerOption probe → {byProbe}); treating the build as Ptr and pausing uploads until resolved."
            );
        }

        return new GameBuildInfo(rawVersion, byVersion, null);
    }
}
