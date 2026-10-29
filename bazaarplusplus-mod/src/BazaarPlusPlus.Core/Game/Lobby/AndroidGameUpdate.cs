#nullable enable
using BazaarPlusPlus.Infrastructure.ReleaseManifest;

namespace BazaarPlusPlus.Game.Lobby;

/// <summary>
/// Whether the game itself is behind the Android Release APK the Platform Release Manifest
/// names. A game update arrives only as a new APK, which Android lets the user install and never
/// the mod, so the answer is a prompt with the APK's link, not an update.
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
    /// The APK to offer when the published one was injected into a newer game than the running
    /// one; null when the game is current, or when either build number is unknown. Both sides
    /// are game version strings in the same format, so neither relies on the APK's versionCode.
    /// </summary>
    public static AndroidApkRelease? Newer(string? runningGameVersion, AndroidApkRelease? published)
    {
        if (published == null)
            return null;
        var running = ParseVersionCode(runningGameVersion);
        var offered = ParseVersionCode(published.GameVersion);
        return running.HasValue && offered.HasValue && offered.Value > running.Value
            ? published
            : null;
    }

    /// <summary>
    /// The new APK this copy needs, and why; null when the Managed Payload or nothing at all
    /// brings it up to date. A newer game always needs the APK. With the same game,
    /// <paramref name="manualUpdate"/> (the self-update decision <c>ManualUpdate</c>) says the BPP
    /// runtime the APK carries (loader, interop, native libraries, Updater) or the history schema
    /// changed. An APK for an older game is never offered.
    /// </summary>
    public static AndroidApkUpdate? Needed(
        string? runningGameVersion,
        AndroidApkRelease? published,
        bool manualUpdate
    )
    {
        if (Newer(runningGameVersion, published) is { } newer)
            return new AndroidApkUpdate(newer, AndroidApkUpdateReason.GameUpdated);
        var running = ParseVersionCode(runningGameVersion);
        return
            manualUpdate
            && published != null
            && running.HasValue
            && ParseVersionCode(published.GameVersion) == running
            ? new AndroidApkUpdate(published, AndroidApkUpdateReason.RuntimeChanged)
            : null;
    }
}

public enum AndroidApkUpdateReason
{
    /// <summary>The published APK carries a newer game.</summary>
    GameUpdated,

    /// <summary>Same game, but the BPP runtime inside the APK changed.</summary>
    RuntimeChanged,
}

/// <summary>An Android Release APK this copy needs, and why.</summary>
public sealed record AndroidApkUpdate(AndroidApkRelease Apk, AndroidApkUpdateReason Reason);
