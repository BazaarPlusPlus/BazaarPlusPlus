#nullable enable
using BazaarPlusPlus.GameInterop.Touch;
using HarmonyLib;
using TheBazaar.UI;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

namespace BazaarPlusPlus.Patches.Input;

/// <summary>
/// The cosmetics Collections shelf on a mobile platform (ADR-0024): the Mac build's
/// <c>CollectionsShelfController.Update</c> reads only the mouse, so a finger never scrolls the
/// shelf. The game's own <c>ProceedMobileDrag</c> reads the first touch instead, as the mobile
/// build does; it needs Enhanced Touch, which the game enables only for its inspection views.
/// </summary>
[HarmonyPatch(typeof(CollectionsShelfController), "Update")]
internal static class TouchCollectionsShelfPatch
{
    [HarmonyPrepare]
    private static bool Prepare() => TouchPlatform.IsMobile;

    [HarmonyPrefix]
    private static bool Prefix(CollectionsShelfController __instance)
    {
        if (__instance.mainCam == null)
            __instance.mainCam = Camera.main;
        if (!EnhancedTouchSupport.enabled)
            EnhancedTouchSupport.Enable();
        __instance.ProceedMobileDrag();
        return false;
    }
}
