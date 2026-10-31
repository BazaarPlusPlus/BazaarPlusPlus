#nullable enable
using BazaarPlusPlus.Game.Tooltips;
using BazaarPlusPlus.GameInterop.Touch;
using HarmonyLib;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus.Patches.Input;

/// <summary>The mobile card press the Mac build compiles out (<see cref="TouchCardPress"/>).</summary>
[HarmonyPatch]
internal static class TouchCardPressPatches
{
    [HarmonyPrepare]
    private static bool Prepare() => TouchPlatform.IsMobile;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardController), nameof(CardController.OnPointerDown))]
    private static void Down(CardController __instance, PointerEventData eventData) =>
        TouchCardPress.Down(__instance, eventData);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardController), nameof(CardController.OnPointerUp))]
    private static void Up(CardController __instance, PointerEventData eventData) =>
        TouchCardPress.Up(__instance, eventData);

    // MainPointerUp runs the click on a mobile platform.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardController), nameof(CardController.OnPointerClick))]
    private static bool Click() => false;

    // A new card's tooltip starts its upgrade toggle at the settings switch.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardController), "ShowTooltips")]
    private static void ShowingTooltips(CardController __instance) =>
        TouchUpgradePreview.BeginTooltip(__instance.CardData);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ItemController), nameof(ItemController.OnBeginDrag))]
    private static void BeganDrag(ItemController __instance) => TouchCardPress.Moved(__instance);
}
