#nullable enable
using System.Collections;
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
        if (Singleton<BoardManager>.Instance?.IsRecapViewOpen == true)
            return false;

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
                    || !ShouldRefreshUpgradePreview(
                        controller.CardData == card,
                        controller.IsCursorOverCard,
                        IsUpgradePreviewActive(config, encounterState)
                    )
                )
                {
                    yield break;
                }

                var tooltipParent = Data.TooltipParentComponent;
                var tooltipController = tooltipParent?.GetCardTooltipController(card);
                if (tooltipParent != null && tooltipController != null)
                {
                    if (
                        RefreshPrimaryTooltipForUpgradePreview(
                            controller,
                            card,
                            tooltipData,
                            tooltipController,
                            config,
                            encounterState
                        )
                    )
                    {
                        yield break;
                    }
                }

                yield return null;
            }
        }
        finally
        {
            PendingControllers.Remove(controller);
        }
    }

    private static bool RefreshPrimaryTooltipForUpgradePreview(
        CardController controller,
        Card card,
        CardTooltipData tooltipData,
        CardTooltipController tooltipController,
        IBppConfig? config,
        IEncounterStateProbe? encounterState
    )
    {
        if (controller == null || tooltipController == null || card is not ItemCard itemCard)
            return false;

        if (
            controller == null
            || !ShouldRefreshUpgradePreview(
                controller.CardData == card,
                controller.IsCursorOverCard,
                IsUpgradePreviewActive(config, encounterState)
            )
        )
        {
            return false;
        }

        var currentTooltipData =
            tooltipController.CurrentTooltipData as CardTooltipData ?? tooltipData;
        return TooltipPreviewContentRefresh.TryApply(
            controller,
            tooltipController,
            itemCard,
            currentTooltipData,
            TooltipPreviewMode.Upgrade,
            TooltipPreviewMode.Normal
        );
    }

    internal static bool ShouldRefreshUpgradePreview(
        bool controllerStillOwnsCard,
        bool isCursorOverCard,
        bool previewActive
    ) => controllerStillOwnsCard && isCursorOverCard && previewActive;
}
