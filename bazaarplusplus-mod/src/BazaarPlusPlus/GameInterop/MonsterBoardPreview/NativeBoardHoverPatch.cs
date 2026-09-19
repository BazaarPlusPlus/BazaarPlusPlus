using BazaarPlusPlus.GameInterop.CardPreview;
using HarmonyLib;
using TheBazaar.UI;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

[HarmonyPatch(typeof(CardPreviewBase), nameof(CardPreviewBase.OnHover))]
internal static class NativeBoardHoverPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardPreviewBase __instance) =>
        NativeCardPreviewHost.ObserveBorrowedHover(__instance, true);
}

[HarmonyPatch(typeof(CardPreviewBase), nameof(CardPreviewBase.OnHoverOut))]
internal static class NativeBoardHoverOutPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardPreviewBase __instance) =>
        NativeCardPreviewHost.ObserveBorrowedHover(__instance, false);
}

[HarmonyPatch(typeof(CardPreviewBase), "OnDestroy")]
internal static class NativeBoardHoverDestroyedPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardPreviewBase __instance) =>
        NativeCardPreviewHost.ForgetBorrowed(__instance);
}
