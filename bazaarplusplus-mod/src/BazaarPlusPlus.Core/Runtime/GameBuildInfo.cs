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

    // Non-null when the two detection signals disagreed or were unreadable; the caller
    // logs it once at startup (resolution happens before BppLog is installed).
    public string? DetectionWarning { get; }
}

/// <summary>
/// Classifies a client from what its host read: the bundleVersion and whether
/// <c>TheBazaar.Config.ServerOption</c> exists (null when the probe was unreadable). Each host
/// reads both from its own runtime; the classification is the same on every host.
/// </summary>
public static class GameBuildInfoResolver
{
    // Primary signal: the PTR build's bundleVersion embeds a "-ptr" token
    // (e.g. "1.0.11358-ptr-macos-arm64-947c079a"); the production build does not.
    // Corroborating probe: only the PTR fork of TheBazaar.Config declares the nested
    // ServerOption class (it holds the PTR server URLs). On disagreement the build is
    // treated as Ptr: misclassifying PTR as Online silently pollutes the production
    // dataset, while misclassifying Online as Ptr only pauses uploads — which the
    // analyzers' staleness alarm surfaces quickly.
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
