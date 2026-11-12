#nullable enable
using BazaarPlusPlus.Infrastructure.ReleaseManifest;

namespace BazaarPlusPlus.Game.Lobby;

/// <summary>
/// An Android game's code identity (root ADR 0016): its versionCode and the sha256 of
/// <c>libil2cpp.so</c> and <c>global-metadata.dat</c>.
/// </summary>
public sealed record AndroidGameCode(long VersionCode, string Il2CppSha256, string MetadataSha256);

/// <summary>How the running game stands against the games the release adapts (root ADR 0016 and 0019).</summary>
public enum AndroidGameAdaptation
{
    /// <summary>Either side is unknown: no Launch Record for this process, or no game in the release.</summary>
    Unknown,

    /// <summary>The same game code as one adapted game.</summary>
    Adapted,

    /// <summary>Other game code, not older than any adapted game.</summary>
    NotAdapted,

    /// <summary>Other game code with a smaller versionCode than some adapted game.</summary>
    GameOlder,
}

/// <summary>
/// How the running game stands, and the adapted game that decided it: the one with the same
/// code for <see cref="AndroidGameAdaptation.Adapted"/>, the newest one for
/// <see cref="AndroidGameAdaptation.GameOlder"/>, and none otherwise.
/// </summary>
public sealed record AndroidGameComparison(
    AndroidGameAdaptation Adaptation,
    AndroidAdaptedGame? Game
);

/// <summary>
/// What an Android copy needs beyond the Managed Payload, against the published release (root
/// ADR 0012 and 0016): a newer game, which the user installs from the game's own store and then
/// starts again from the Android App, or a new Android App, whose runtime the release needs.
/// Neither is something the mod can install, so the answer is a prompt, not an update.
/// </summary>
public static class AndroidGameUpdate
{
    /// <summary>
    /// Compares the running game's code (the Launch Record's <c>game</c>) with every game the
    /// release adapts (<c>adaptations</c>). Only the code decides, never versionName: a
    /// content-only game update keeps the same code. A match with any adapted game is adapted,
    /// whatever the others' versionCodes; otherwise the game is older when some adapted game has
    /// a larger versionCode, the newest of them named as the update.
    /// </summary>
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

    /// <summary>
    /// What this copy needs, and why; null when the Managed Payload or nothing at all brings it
    /// up to date. An older game always comes first. With an adapted game,
    /// <paramref name="manualUpdate"/> (the self-update decision <c>ManualUpdate</c>) says the
    /// release needs another runtime than the running Android App carries (loader, native
    /// libraries, Updater) or another history schema, so the new App is offered when the release
    /// names one. The update names the game that decided the comparison.
    /// </summary>
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
    /// <summary>The release was adapted for a newer game: update the game from its store.</summary>
    GameUpdated,

    /// <summary>Same game, but the release needs a new Android App.</summary>
    AppUpdated,
}

/// <summary>Where the published Android App APK downloads from, and its size.</summary>
public sealed record AndroidAppDownload(Uri Url, long Size);

/// <summary>
/// What this copy needs, and why; <see cref="App"/> is the download for
/// <see cref="AndroidUpdateReason.AppUpdated"/> and null for a game update.
/// </summary>
public sealed record AndroidUpdate(
    AndroidUpdateReason Reason,
    string PublishedGameVersion,
    AndroidAppDownload? App
);
