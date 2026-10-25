using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Android.Host;

/// <summary>
/// The settings only the Android host has, in the <c>[Android]</c> section of the same
/// <c>BazaarPlusPlus.cfg</c> that holds the shared <see cref="BppSettings"/>. Touch replaces the
/// desktop's hotkeys: the upgrade preview, held on Shift there, is a persisted switch here.
/// </summary>
internal sealed class AndroidSettings
{
    public AndroidSettings(IBppSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        TouchMenu = store.Bind(
            "Android",
            "TouchMenu",
            true,
            "Whether the native-style icon shortcuts for History, Collection, and Ten-Win Builds are shown."
        );
        CombatTimer = store.Bind(
            "Android",
            "CombatTimer",
            true,
            "Show the touch combat timer and playback controls only during combat or replay."
        );
        UpgradePreview = store.Bind(
            "Android",
            "UpgradePreview",
            false,
            "Whether item tooltips automatically show the next tier's values. Each tooltip also offers a touch toggle."
        );
        CollectionPanel = store.Bind(
            "Android",
            "CollectionPanel",
            true,
            "Whether the icon shortcuts offer the Collection panel, which shows the game's own cards."
        );
        HistoryPanel = store.Bind(
            "Android",
            "HistoryPanel",
            true,
            "Whether the icon shortcuts offer the History panel: past runs, Ghost battles, favorites, and their native boards."
        );
        LiveBuildPanel = store.Bind(
            "Android",
            "LiveBuildPanel",
            true,
            "Whether the icon shortcuts offer the Live Build panel, which recommends ten-win builds for the run."
        );
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

    public ISetting<bool> TouchMenu { get; }

    public ISetting<bool> CollectionPanel { get; }

    public ISetting<bool> HistoryPanel { get; }

    public ISetting<bool> LiveBuildPanel { get; }

    public ISetting<bool> UpgradePreview { get; }

    public ISetting<bool> CombatTimer { get; }
}
