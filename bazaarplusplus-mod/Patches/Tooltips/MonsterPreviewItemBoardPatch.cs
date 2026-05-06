#pragma warning disable CS0436
#nullable enable
using BazaarPlusPlus.Game.MonsterPreview;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), "ShowTooltips")]
internal static class MonsterPreviewItemBoardShowTooltipsPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardController __instance)
    {
        return !MonsterPreviewItemBoardRuntime.Instance?.ShouldSuppressTooltip(__instance?.CardData)
            ?? true;
    }
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.LockTooltipToggle))]
internal static class MonsterPreviewItemBoardLockTogglePatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardTooltipController __instance)
    {
        var currentCard = __instance?.CurrentCard;
        var runtime = MonsterPreviewItemBoardRuntime.Instance;
        if (
            runtime != null
            && runtime.TryConsumeNextClickToClosePreview(
                UnityEngine.EventSystems.PointerEventData.InputButton.Right,
                "next right click"
            )
        )
        {
            return false;
        }

        if (
            runtime != null
            && runtime.ShouldInterceptLockToggle(currentCard)
            && runtime.HandleLockToggle(__instance)
        )
        {
            return false;
        }

        return true;
    }
}
