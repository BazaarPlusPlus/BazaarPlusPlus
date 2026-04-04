#pragma warning disable CS0436
using HarmonyLib;
using UnityEngine.EventSystems;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), "ProceedClick")]
internal static class ShowcaseCardClickPatch
{
    [HarmonyPrefix] // disable showcase card click
    private static bool Prefix(CardController __instance, PointerEventData eventData)
    {
        if (__instance == null || __instance.GetComponent<ShowcaseCardMarker>() == null)
            return true;

        return false;
    }
}
