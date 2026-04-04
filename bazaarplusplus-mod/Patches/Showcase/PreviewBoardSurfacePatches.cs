#pragma warning disable CS0436
using System.Linq;
using BazaarPlusPlus.Game.MonsterPreview;
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), "IsPointerOverThis")]
internal static class PreviewBoardSurfaceBlocksUnderlyingCardsPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardController __instance, Vector2 screenPos, ref bool __result)
    {
        if (!__result || __instance == null || Camera.main == null)
            return;

        if (__instance.GetComponent<ShowcaseCardMarker>() != null)
            return;

        var hits = Physics
            .RaycastAll(Camera.main.ScreenPointToRay(screenPos), float.MaxValue)
            .OrderBy(hit => hit.distance)
            .ToArray();
        if (hits.Length == 0)
            return;

        var firstTransform = hits[0].transform;
        if (firstTransform == null)
            return;

        if (firstTransform.GetComponentInParent<PreviewBoardSurfaceMarker>() == null)
            return;

        __result = false;
    }
}
