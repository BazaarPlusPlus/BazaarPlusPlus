using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Android.Host;

internal sealed class AndroidDevSettings
{
    public AndroidDevSettings(IBppSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        BuildCorpusPort = store.Bind(
            "AndroidDev",
            "BuildCorpusPort",
            0,
            "Developer testing only. When above 0, the ten-win build catalog fetches its corpus from http://127.0.0.1:<port> (a local server reached through adb reverse) instead of the BPP CDN. 0 is off.",
            new SettingRange<int>(0, 65535)
        );
    }

    public ISetting<int> BuildCorpusPort { get; }
}
