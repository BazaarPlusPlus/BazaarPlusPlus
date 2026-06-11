#nullable enable

using System;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Infrastructure;
using CombatStatusBarFeature = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.OverlayPanels;

internal static class BppUiChromeSuppression
{
    public static IDisposable? Begin(BppUiChromeSuppressionMode mode)
    {
        return mode switch
        {
            BppUiChromeSuppressionMode.Screenshot => UiSuppressionScope.Begin(
                CollectionPanelDockButtonController.BeginScreenshotSuppression,
                BppSettingsDockController.BeginScreenshotSuppression,
                CombatStatusBarFeature.BeginScreenshotSuppression
            ),
            BppUiChromeSuppressionMode.ReplayRecording => UiSuppressionScope.Begin(
                CollectionPanelDockButtonController.BeginScreenshotSuppression,
                BppSettingsDockController.BeginScreenshotSuppression
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unknown BPP UI chrome suppression mode."
            ),
        };
    }
}
