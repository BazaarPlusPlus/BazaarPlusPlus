#pragma warning disable CS0436
#nullable enable
using System.Reflection;
using BazaarPlusPlus.Game.MonsterPreview;
using BazaarPlusPlus.Game.Tooltips;
using HarmonyLib;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), "ShowTooltips")]
internal static class NativeMonsterPreviewShowTooltipsPatch
{
    private static readonly FieldInfo? CardTooltipDataField = AccessTools.Field(
        typeof(CardController),
        "_cardTooltipData"
    );

    [HarmonyPrefix]
    private static void Prefix(CardController __instance)
    {
        TryAugmentCachedTooltipData(__instance, "show_tooltips");
    }

    internal static CardTooltipData? TryAugmentCachedTooltipData(
        CardController? controller,
        string reason
    )
    {
        if (
            controller == null
            || CardTooltipDataField?.GetValue(controller) is not CardTooltipData tooltipData
            || CardTooltipDataFactory.GetMonster(tooltipData) != null
        )
        {
            return null;
        }

        if (
            !NativeMonsterTooltipAugmenter.TryAugment(
                tooltipData.CardInstance,
                tooltipData,
                out var augmentedTooltipData,
                reason
            )
            || augmentedTooltipData == null
        )
        {
            return null;
        }

        CardTooltipDataField.SetValue(controller, augmentedTooltipData);
        return augmentedTooltipData;
    }
}

[HarmonyPatch(typeof(CardController), nameof(CardController.GetTooltipData))]
internal static class NativeMonsterPreviewGetTooltipDataPatch
{
    [HarmonyPostfix]
    private static void Postfix(CardController __instance, ref ITooltipData __result)
    {
        if (__result is not CardTooltipData)
            return;

        var augmentedTooltipData =
            NativeMonsterPreviewShowTooltipsPatch.TryAugmentCachedTooltipData(
                __instance,
                "get_tooltip_data"
            );
        if (augmentedTooltipData != null)
            __result = augmentedTooltipData;
    }
}
