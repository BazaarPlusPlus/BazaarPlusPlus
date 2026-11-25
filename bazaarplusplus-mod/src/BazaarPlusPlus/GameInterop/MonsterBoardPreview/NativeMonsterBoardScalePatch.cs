#nullable enable
using HarmonyLib;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

[HarmonyPatch(typeof(MonsterBoardRenderer), nameof(MonsterBoardRenderer.Initialize))]
internal static class NativeMonsterBoardScalePatch
{
    private const float HorizontalScale = 0.95f;

    [HarmonyPostfix]
    private static void Postfix(MonsterBoardTooltip view)
    {
        if (view == null || view._carpetImage == null)
            return;

        // The centered carpet also contains the card sockets. Skills and health are siblings.
        var carpet = view._carpetImage.rectTransform;
        var scale = carpet.localScale;
        scale.x *= HorizontalScale;
        carpet.localScale = scale;
    }
}
