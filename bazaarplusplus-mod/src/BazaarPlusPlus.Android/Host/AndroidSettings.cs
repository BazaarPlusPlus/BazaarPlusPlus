using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// The settings only the Android host has, in the same <c>BazaarPlusPlus.cfg</c> that holds the
/// shared <see cref="BppSettings"/> and the Mobile UI's <c>[Touch]</c> switch.
/// </summary>
internal sealed class AndroidSettings
{
    public AndroidSettings(IBppSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        DevReleaseManifestPort = store.Bind(
            "Dev",
            "ReleaseManifestPort",
            0,
            "Developer device tests only; leave 0. A port above 0 reads the release manifest from http://127.0.0.1:<port>/latest/android-aarch64.json (a local server reached through adb reverse) instead of the published one."
        );
    }

    /// <summary>
    /// The developer seam for the game-update row: 0, the default, reads the published manifest
    /// (<see cref="Lobby.AndroidVersionCheck.DevManifest"/>).
    /// </summary>
    public ISetting<int> DevReleaseManifestPort { get; }
}
