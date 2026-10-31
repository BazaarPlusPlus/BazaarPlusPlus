#nullable enable
using BazaarPlusPlus.Game.Settings;
using BazaarPlusPlus.Game.Touch;
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
                BppPanelDockButtonsController.BeginScreenshotSuppression,
                TouchShortcutSuppression.Begin,
                CombatStatusBarFeature.BeginScreenshotSuppression
            ),
            BppUiChromeSuppressionMode.ReplayRecording => UiSuppressionScope.Begin(
                BppPanelDockButtonsController.BeginScreenshotSuppression
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unknown BPP UI chrome suppression mode."
            ),
        };
    }
}
