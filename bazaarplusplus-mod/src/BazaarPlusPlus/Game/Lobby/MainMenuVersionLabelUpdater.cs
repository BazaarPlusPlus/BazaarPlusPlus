#nullable enable
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby;

internal static class MainMenuVersionLabelUpdater
{
    private static TextMeshProUGUI? _lastLabel;

    public static void Refresh(TextMeshProUGUI? versionLabel)
    {
        if (versionLabel == null)
            return;

        _lastLabel = versionLabel;
        var gameVersion = Application.version;
        if (HostPlatformKey.Current == HostPlatformKey.Ios)
            gameVersion = MainMenuVersionLabelFormatter.IosGameVersion(gameVersion);
        var text = MainMenuVersionLabelFormatter.Build(
            gameVersion,
            BppPluginVersion.Current,
            MainMenuVersionUpdateState.Current.Status
        );
        versionLabel.text = text;
    }

    public static void RefreshCurrent()
    {
        if (_lastLabel == null)
            return;

        Refresh(_lastLabel);
    }
}
