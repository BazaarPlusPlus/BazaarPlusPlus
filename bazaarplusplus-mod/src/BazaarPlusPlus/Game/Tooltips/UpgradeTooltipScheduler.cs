#nullable enable
using System.Collections;
using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Core.Config;
using BazaarPlusPlus.Core.GameState;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class UpgradeTooltipScheduler
{
    private static readonly HashSet<CardController> PendingControllers =
        new HashSet<CardController>();

    private static bool IsUpgradePreviewActive(
        IBppConfig? config,
        IEncounterStateProbe? encounterState
    )
    {
        return TooltipPreviewModePolicy.Resolve(config, encounterState)
            == TooltipPreviewMode.Upgrade;
    }

    internal static bool TryScheduleUpgradeTooltip(
        CardController controller,
        IBppConfig? config,
        IEncounterStateProbe? encounterState,
        CardTooltipData? tooltipData = null
    )
    {
        if (controller == null)
            return false;

        if (!IsUpgradePreviewActive(config, encounterState))
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
            RefreshUpgradePreviewWhenReady(
                controller,
                card,
                resolvedTooltipData,
                config,
                encounterState
            )
        );
        return true;
    }

    private static IEnumerator RefreshUpgradePreviewWhenReady(
        CardController controller,
        Card card,
        CardTooltipData tooltipData,
        IBppConfig? config,
        IEncounterStateProbe? encounterState
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
                    || !IsUpgradePreviewActive(config, encounterState)
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
                        tooltipParent,
                        config,
                        encounterState
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
        TooltipParentComponent tooltipParent,
        IBppConfig? config,
        IEncounterStateProbe? encounterState
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
            || !IsUpgradePreviewActive(config, encounterState)
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
