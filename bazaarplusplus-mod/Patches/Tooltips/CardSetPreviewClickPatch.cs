#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.MonsterPreview;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), nameof(CardController.OnPointerClick))]
internal static class CardSetPreviewClickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardController __instance, PointerEventData eventData)
    {
        return !(
            CardSetPreviewRuntime.Instance?.TryHandleCardClick(__instance, eventData) ?? false
        );
    }
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.LockTooltipToggle))]
internal static class CardSetPreviewLockTogglePatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardTooltipController __instance)
    {
        return !(
            CardSetPreviewRuntime.Instance?.ShouldSuppressNativeLockToggle(__instance?.CurrentCard)
            ?? false
        );
    }
}
