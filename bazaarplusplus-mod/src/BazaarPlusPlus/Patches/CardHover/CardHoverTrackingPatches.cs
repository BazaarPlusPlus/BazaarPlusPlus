#nullable enable
using BazaarPlusPlus.GameInterop.Cards;
using HarmonyLib;

namespace BazaarPlusPlus.Patches.CardHover;

// Postfixes still run when a prefix suppresses the original (e.g. native-tooltip
// suppression); the tracker tolerates that because reads re-validate IsCursorOverCard,
// which only the un-suppressed original sets.

[HarmonyPatch(typeof(CardController), nameof(CardController.OnPointerEnter))]
internal static class CardHoverPointerEnterPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardController __instance)
    {
        HoveredCardTracker.NotifyPointerEnter(__instance);
    }
}

[HarmonyPatch(typeof(CardController), nameof(CardController.OnPointerExit))]
internal static class CardHoverPointerExitPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardController __instance)
    {
        HoveredCardTracker.NotifyPointerExit(__instance);
    }
}
