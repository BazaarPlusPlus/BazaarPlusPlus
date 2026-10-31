#nullable enable
using BazaarPlusPlus.Core.Runtime;
using BazaarPlusPlus.Game.CollectionPanel;
using BazaarPlusPlus.Game.LiveBuildPanel;
using BazaarPlusPlus.GameInterop.Touch;
using BazaarPlusPlus.Host;
using BazaarPlusPlus.Mobile.Hud;
using BazaarPlusPlus.Mobile.NativeAdjustment;
using BazaarPlusPlus.Mobile.Previews;
using BazaarPlusPlus.Patches.ScreenFill;
using UnityEngine;
using CombatClock = BazaarPlusPlus.Game.CombatStatusBar.CombatStatusBar;

namespace BazaarPlusPlus.Game.Touch;

/// <summary>
/// The Mobile UI's HUD on a mobile platform (ADR-0025), in place of the desktop's dock buttons and
/// combat status bar: its shortcuts open the touch Collection and Ten-Win Builds the panel mounts
/// own, its clock reads the desktop bar's state, which <see cref="CombatClock"/> still feeds, and
/// its preview switches take the place of the desktop's Shift and Ctrl hold keys in the item
/// tooltip, and its Screen Fill lets the game fill a screen narrower than 16:10, such as an
/// iPad's, instead of letterboxing it. It mounts after the Overlay Panel Host, which ticks it,
/// and after those panels.
/// </summary>
internal sealed class TouchHudMount : IBppMountable
{
    private TouchHud? _hud;

    public void Mount(GameObject host, IBppServices services)
    {
        var screenFill = new TouchScreenFill(new TouchScreenFillHost());
        _hud = new TouchHud(
            new TouchHudData(
                CollectionPanelMount.OpenTouchPanel,
                LiveBuildPanelMount.OpenTouchPanel,
                TouchShortcutSuppression.Shown,
                new TouchCombatClockData(
                    CombatClock.State,
                    () => services.Config.EnableCombatStatusBarConfig.Value,
                    CombatClock.ShowsTouchClock,
                    static () =>
                    {
                        if (CombatClock.State.CanToggleCombatPause())
                            CombatClock.State.ToggleCombatPause();
                    }
                ),
                new TouchPreviewSwitches(new TouchTooltipPreviewHost()),
                screenFill
            )
        );
        TouchScreenFillCameraPatch.ScreenFill = screenFill;
        // An end-of-run screenshot hides the HUD in the frame it begins in.
        TouchShortcutSuppression.Hide = _hud.Hide;
    }

    public void Unmount(GameObject host)
    {
        TouchShortcutSuppression.Hide = null;
        TouchScreenFillCameraPatch.ScreenFill = null;
        _hud?.Dispose();
        _hud = null;
    }
}
