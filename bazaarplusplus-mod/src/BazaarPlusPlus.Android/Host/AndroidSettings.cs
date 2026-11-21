using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Android.Host;

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
        FrameRatePriority = store.Bind(
            "Graphics",
            "FrameRatePriority",
            false,
            "Keep the game at its own frame-rate setting under load. The game then lowers only the render resolution, as it does without BazaarPlusPlus. Off leaves the game's own frame-rate scaling on."
        );
        RenderScale = store.Bind(
            "Graphics",
            "RenderScale",
            100,
            "Resolution the game's cameras render at, as a percent of the screen per axis. Lower values draw fewer pixels and can raise the frame rate on a slow GPU. 100 leaves the game's own value.",
            new SettingRange<int>(50, 100)
        );
    }

    public ISetting<bool> FrameRatePriority { get; }

    public ISetting<int> RenderScale { get; }

    public ISetting<int> DevReleaseManifestPort { get; }
}
