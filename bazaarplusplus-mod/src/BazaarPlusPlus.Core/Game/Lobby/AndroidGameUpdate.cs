#nullable enable
namespace BazaarPlusPlus.Game.Lobby;

/// <summary>
/// What an Android copy needs beyond the Managed Payload, against the published release (root
/// ADR 0012): a newer game, which the user installs from the game's own store and then starts
/// again from the Android App, or a new Android App, whose runtime the release needs. Neither is
/// something the mod can install, so the answer is a prompt, not an update.
/// </summary>
public static class AndroidGameUpdate
{
    /// <summary>
    /// The build number inside an Android game version such as
    /// <c>1.0.12825-production-android-d9601ded</c>: the third dotted number, 12825.
    /// </summary>
    public static int? ParseVersionCode(string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
            return null;
        var numbers = gameVersion!.Trim().Split('-')[0].Split('.');
        return numbers.Length == 3 && int.TryParse(numbers[2], out var code) && code > 0
            ? code
            : null;
    }

    /// <summary>
    /// Whether the published release was adapted for a newer game than the running one; false
    /// when either build number is unknown. Both sides are game version strings in the same
    /// format.
    /// </summary>
    public static bool Newer(string? runningGameVersion, string? publishedGameVersion)
    {
        var running = ParseVersionCode(runningGameVersion);
        var offered = ParseVersionCode(publishedGameVersion);
        return running.HasValue && offered.HasValue && offered.Value > running.Value;
    }

    /// <summary>
    /// What this copy needs, and why; null when the Managed Payload or nothing at all brings it
    /// up to date. A newer game always comes first. With the same game,
    /// <paramref name="manualUpdate"/> (the self-update decision <c>ManualUpdate</c>) says the
    /// release needs another runtime than the running Android App carries (loader, native
    /// libraries, Updater) or another history schema, so the new App is offered when the release
    /// names one. A release for an older game asks for nothing.
    /// </summary>
    public static AndroidUpdate? Needed(
        string? runningGameVersion,
        string? publishedGameVersion,
        AndroidAppDownload? app,
        bool manualUpdate
    )
    {
        if (Newer(runningGameVersion, publishedGameVersion))
            return new AndroidUpdate(AndroidUpdateReason.GameUpdated, publishedGameVersion!, null);
        var running = ParseVersionCode(runningGameVersion);
        return
            manualUpdate
            && app != null
            && running.HasValue
            && ParseVersionCode(publishedGameVersion) == running
            ? new AndroidUpdate(AndroidUpdateReason.AppUpdated, publishedGameVersion!, app)
            : null;
    }
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
