#nullable enable
using BazaarPlusPlus.Infrastructure.ReleaseManifest;

namespace BazaarPlusPlus.Game.Lobby;

public sealed record AndroidGameCode(long VersionCode, string Il2CppSha256, string MetadataSha256);

public enum AndroidGameAdaptation
{
    Unknown,

    Adapted,

    NotAdapted,

    GameOlder,
}

public sealed record AndroidGameComparison(
    AndroidGameAdaptation Adaptation,
    AndroidAdaptedGame? Game
);

// Game and App updates must be installed by the player because they replace components outside the Managed Payload.
public static class AndroidGameUpdate
{
    // Compare code hashes because content-only updates can change the version name without changing the code.
    public static AndroidGameComparison Compare(
        AndroidGameCode? running,
        IReadOnlyList<AndroidAdaptedGame>? adapted
    )
    {
        if (running == null || adapted == null || adapted.Count == 0)
            return new AndroidGameComparison(AndroidGameAdaptation.Unknown, null);
        foreach (var game in adapted)
        {
            if (
                string.Equals(running.Il2CppSha256, game.Il2CppSha256, StringComparison.Ordinal)
                && string.Equals(
                    running.MetadataSha256,
                    game.MetadataSha256,
                    StringComparison.Ordinal
                )
            )
                return new AndroidGameComparison(AndroidGameAdaptation.Adapted, game);
        }
        AndroidAdaptedGame? newer = null;
        foreach (var game in adapted)
        {
            if (
                game.VersionCode > running.VersionCode
                && game.VersionCode > (newer?.VersionCode ?? 0)
            )
                newer = game;
        }
        return newer == null
            ? new AndroidGameComparison(AndroidGameAdaptation.NotAdapted, null)
            : new AndroidGameComparison(AndroidGameAdaptation.GameOlder, newer);
    }

    public static AndroidUpdate? Needed(
        AndroidGameComparison comparison,
        AndroidAppDownload? app,
        bool manualUpdate
    ) =>
        comparison.Adaptation switch
        {
            AndroidGameAdaptation.GameOlder => new AndroidUpdate(
                AndroidUpdateReason.GameUpdated,
                comparison.Game?.GameVersion ?? string.Empty,
                null
            ),
            AndroidGameAdaptation.Adapted when manualUpdate && app != null => new AndroidUpdate(
                AndroidUpdateReason.AppUpdated,
                comparison.Game?.GameVersion ?? string.Empty,
                app
            ),
            _ => null,
        };
}

public enum AndroidUpdateReason
{
    GameUpdated,

    AppUpdated,
}

public sealed record AndroidAppDownload(Uri Url, long Size);

public sealed record AndroidUpdate(
    AndroidUpdateReason Reason,
    string PublishedGameVersion,
    AndroidAppDownload? App
);
