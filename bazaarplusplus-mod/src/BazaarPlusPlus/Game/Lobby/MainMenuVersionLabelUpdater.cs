#nullable enable
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Fonts;
using TMPro;
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
        var text = MainMenuVersionLabelFormatter.Build(
            Application.version,
            BppPluginVersion.Current,
            MainMenuVersionUpdateState.Current.UpdateAvailable
        );
        versionLabel.text = text;
        BppTmpFont.TryApply(versionLabel, text);
    }

    public static void RefreshCurrent()
    {
        if (_lastLabel == null)
            return;

        Refresh(_lastLabel);
    }
}
