#nullable enable
#pragma warning disable CS0436
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.PostCombatImpact;
using BazaarPlusPlus.GameInterop.Tooltips;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Patches.PostCombatImpact;

[HarmonyPatch(typeof(RecapItemVisualController), "ShowTooltip")]
internal static class PostCombatImpactRecapShowTooltipPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        RecapItemVisualController __instance,
        Card ___CardData,
        CardTooltipData? ___cardTooltipData,
        Vector3 ___tooltipOffset
    )
    {
        if (NativeTooltipSuppression.IsActive || ___CardData == null)
            return;

        BppPatchHost.Features.PostCombatImpact.SetHoveredRecapCard(
            __instance,
            ___CardData,
            ___cardTooltipData,
            ___tooltipOffset
        );
    }
}

[HarmonyPatch(typeof(RecapItemVisualController), "OnDisable")]
internal static class PostCombatImpactRecapDisablePatch
{
    [HarmonyPrefix]
    private static void Prefix(RecapItemVisualController __instance) =>
        BppPatchHost.Features.PostCombatImpact.ClearHoveredRecapCard(
            __instance,
            PostCombatImpactHoverExitOrigin.RecapDisabled,
            __instance.HoveredTooltipIsLocked()
        );
}

[HarmonyPatch(typeof(RecapItemVisualController), nameof(RecapItemVisualController.OnPointerExit))]
internal static class PostCombatImpactRecapPointerExitPatch
{
    [HarmonyPrefix]
    private static void Prefix(RecapItemVisualController __instance, out bool __state)
    {
        __state = __instance.HoveredTooltipIsLocked();
        BppPatchHost.Features.PostCombatImpact.ClearHoveredRecapCard(
            __instance,
            PostCombatImpactHoverExitOrigin.RecapPointerExit,
            __state
        );
    }

    [HarmonyPostfix]
    private static void Postfix(
        RecapItemVisualController __instance,
        bool __state,
        ref bool ___IsHovering
    )
    {
        if (!__state || !___IsHovering)
            return;

        // Native OnPointerExit skips the entire visual reset while its tooltip is locked.
        // Combat impact temporarily owns that lock, so restore the native hover state without
        // unlocking or disrupting the paired-tooltip dismissal grace period.
        ___IsHovering = false;
        __instance.Move();
    }
}

[HarmonyPatch(typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerEnter))]
internal static class PostCombatImpactSkillPointerEnterPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        SkillProxyRenderer __instance,
        CardTooltipData? ____tooltipData,
        Vector3 ____tooltipOffsetWorldSpace
    )
    {
        if (NativeTooltipSuppression.IsActive)
            return;

        var boardManager = Singleton<BoardManager>.Instance;
        var card = __instance.Card;
        if (card == null || boardManager == null || !boardManager.IsRecapViewOpen)
            return;

        BppPatchHost.Features.PostCombatImpact.SetHoveredSkill(
            __instance,
            card,
            ____tooltipData,
            ____tooltipOffsetWorldSpace
        );
    }
}

[HarmonyPatch(typeof(SkillProxyRenderer), nameof(SkillProxyRenderer.OnPointerExit))]
internal static class PostCombatImpactSkillPointerExitPatch
{
    [HarmonyPrefix]
    private static void Prefix(SkillProxyRenderer __instance)
    {
        var tooltipParent = Data.TooltipParentComponent;
        var cardTooltip =
            __instance.Card == null
                ? null
                : tooltipParent?.GetCardTooltipController(__instance.Card);
        BppPatchHost.Features.PostCombatImpact.ClearHoveredSkill(
            __instance,
            PostCombatImpactHoverExitOrigin.SkillPointerExit,
            cardTooltip != null && tooltipParent?.IsCardTooltipControllerLocked(cardTooltip) == true
        );
    }
}

[HarmonyPatch(
    typeof(AuxiliaryTooltipController),
    nameof(AuxiliaryTooltipController.ShowAuxiliaryTooltipController)
)]
internal static class PostCombatImpactAuxiliaryTooltipShowPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        AuxiliaryTooltipController __instance,
        Transform worldSpaceTransform,
        string newHeader,
        string newBodyText
    )
    {
        if (NativeTooltipSuppression.IsActive)
            return;

        BppPatchHost.Features.PostCombatImpact.OnNativeAuxiliaryTooltipShowing(
            __instance,
            worldSpaceTransform,
            newHeader,
            newBodyText
        );
    }
}

[HarmonyPatch(
    typeof(AuxiliaryTooltipController),
    nameof(AuxiliaryTooltipController.StartTooltipFadeOut)
)]
internal static class PostCombatImpactAuxiliaryTooltipHidePatch
{
    [HarmonyPrefix]
    private static void Prefix(AuxiliaryTooltipController __instance) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeAuxiliaryTooltipHiding(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ShowTooltipController))]
internal static class PostCombatImpactTooltipPreparePatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance, ITooltipData iTooltipData)
    {
        if (!NativeTooltipSuppression.IsActive)
            BppPatchHost.Features.PostCombatImpact.OnNativeTooltipPreparing(
                __instance,
                iTooltipData
            );
    }
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ResetValues))]
internal static class PostCombatImpactTooltipResetPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeTooltipChanging(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), nameof(CardTooltipController.ClearCurrentCard))]
internal static class PostCombatImpactTooltipClearPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeTooltipChanging(__instance);
}

[HarmonyPatch(typeof(CardTooltipController), "OnDisable")]
internal static class PostCombatImpactTooltipDisablePatch
{
    [HarmonyPrefix]
    private static void Prefix(CardTooltipController __instance) =>
        BppPatchHost.Features.PostCombatImpact.OnNativeTooltipChanging(__instance);
}

[HarmonyPatch(typeof(BaseTooltipController), "ToggleInteractabilityOnCanvas")]
internal static class PostCombatImpactTooltipRaycastPassPatch
{
    [HarmonyPostfix]
    private static void Postfix(BaseTooltipController __instance, CanvasGroup ___tooltipCanvasGroup)
    {
        if (___tooltipCanvasGroup == null)
            return;

        BppPatchHost.Features.PostCombatImpact.OnNativeTooltipInteractabilityChanged(
            __instance,
            ___tooltipCanvasGroup
        );
    }
}
