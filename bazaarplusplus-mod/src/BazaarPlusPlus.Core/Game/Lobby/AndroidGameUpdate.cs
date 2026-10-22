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
    /// The version code inside an Android <c>Application.version</c> such as
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
    /// one; null when the game is current, or when either version code is unknown.
    /// </summary>
    public static AndroidApkRelease? Newer(string? runningGameVersion, AndroidApkRelease? published)
    {
        var running = ParseVersionCode(runningGameVersion);
        return published != null && running.HasValue && published.GameVersionCode > running.Value
            ? published
            : null;
    }
}
