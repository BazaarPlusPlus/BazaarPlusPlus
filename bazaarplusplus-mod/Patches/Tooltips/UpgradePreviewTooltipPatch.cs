#pragma warning disable CS0436
#nullable enable
using System.Collections;
using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Game.Input;
using BazaarPlusPlus.Game.Tooltips;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(CardController), "ShowTooltips")]
internal static class UpgradePreviewTooltipPatch
{
    private static readonly HashSet<CardController> PendingControllers =
        new HashSet<CardController>();

    [HarmonyPostfix]
    private static void Postfix(CardController __instance)
    {
        TryScheduleUpgradeTooltip(__instance);
    }

    internal static bool TryScheduleUpgradeTooltip(
        CardController controller,
        CardTooltipData? tooltipData = null
    )
    {
        if (controller == null)
            return false;

        if (!BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview))
            return false;

        var card = controller.CardData;
        if (card is not ItemCard)
            return false;

        if (!card.CanCardUpgrade())
            return false;

        var resolvedTooltipData = tooltipData ?? controller.GetTooltipData() as CardTooltipData;
        if (resolvedTooltipData == null)
            return false;

        if (Data.TooltipParentComponent == null)
            return false;

        if (!PendingControllers.Add(controller))
            return false;

        controller.StartCoroutine(
            RefreshUpgradePreviewWhenReady(controller, card, resolvedTooltipData)
        );
        return true;
    }

    private static IEnumerator RefreshUpgradePreviewWhenReady(
        CardController controller,
        Card card,
        CardTooltipData tooltipData
    )
    {
        try
        {
            const int maxFramesToWait = 10;
            for (var i = 0; i < maxFramesToWait; i++)
            {
                if (
                    controller == null
                    || controller.CardData != card
                    || !BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview)
                )
                {
                    yield break;
                }

                var tooltipParent = Data.TooltipParentComponent;
                if (tooltipParent != null && tooltipParent.GetCardTooltipController(card) != null)
                {
                    RefreshPrimaryTooltipForUpgradePreview(
                        controller,
                        card,
                        tooltipData,
                        tooltipParent
                    );
                    yield break;
                }

                yield return null;
            }
        }
        finally
        {
            PendingControllers.Remove(controller);
        }
    }

    private static void RefreshPrimaryTooltipForUpgradePreview(
        CardController controller,
        Card card,
        CardTooltipData tooltipData,
        TooltipParentComponent tooltipParent
    )
    {
        if (controller == null || tooltipParent == null)
            return;

        if (tooltipParent.GetCardTooltipController(card) == null)
            return;

        var refreshedTooltipData = CardTooltipDataFactory.Create(card, tooltipData);
        tooltipParent.HideCardTooltipController();

        if (
            controller == null
            || controller.CardData != card
            || !BppHotkeyService.IsHeld(BppHotkeyActionId.HoldUpgradePreview)
        )
        {
            return;
        }

        controller.EnterUpgradePreview();
        tooltipParent.ShowCardTooltipController(
            controller.transform,
            controller.TooltipOffset,
            refreshedTooltipData
        );
    }
}
