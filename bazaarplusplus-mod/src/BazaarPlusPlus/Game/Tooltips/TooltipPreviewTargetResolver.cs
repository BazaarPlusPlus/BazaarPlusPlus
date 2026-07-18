#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar;
using TheBazaar.Tooltips;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class TooltipPreviewTargetResolver
{
    internal readonly struct TooltipRefreshTarget
    {
        public TooltipRefreshTarget(
            CardController controller,
            ItemCard card,
            CardTooltipData tooltipData
        )
        {
            Controller = controller;
            Card = card;
            TooltipData = tooltipData;
        }

        public CardController Controller { get; }

        public ItemCard Card { get; }

        public CardTooltipData TooltipData { get; }
    }

    internal static bool TryResolveCurrentPrimaryItemTooltip(
        TooltipParentComponent tooltipParent,
        out TooltipRefreshTarget target
    )
    {
        target = default;

        if (tooltipParent == null || Data.CardAndSkillLookup == null)
            return false;

        var primaryController = Traverse
            .Create(tooltipParent)
            .Property("CardTooltipController")
            .GetValue<CardTooltipController>();
        if (primaryController == null)
        {
            Report(
                TooltipPreviewTargetOutcome.Skipped,
                TooltipLogReasonCode.NoPrimaryController,
                card: null
            );
            return false;
        }

        var tooltipData = TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
            primaryController.CurrentTooltipData,
            primaryController.CurrentCard
        );
        if (tooltipData?.CardInstance is not ItemCard itemCard)
        {
            Report(
                TooltipPreviewTargetOutcome.Skipped,
                TooltipLogReasonCode.NoItemTooltipData,
                primaryController.CurrentCard
            );
            return false;
        }

        var cardController = TryResolveCardController(itemCard, primaryController.CurrentCard);
        if (cardController?.CardData is not ItemCard controllerItemCard)
        {
            Report(
                TooltipPreviewTargetOutcome.Skipped,
                TooltipLogReasonCode.NoCardController,
                itemCard
            );
            return false;
        }

        if (!TooltipPreviewTargetSelection.AreSameCard(controllerItemCard, itemCard))
        {
            Report(
                TooltipPreviewTargetOutcome.Skipped,
                TooltipLogReasonCode.ControllerCardMismatch,
                itemCard
            );
            return false;
        }

        target = new TooltipRefreshTarget(cardController, controllerItemCard, tooltipData);
        Report(
            TooltipPreviewTargetOutcome.Resolved,
            TooltipLogReasonCode.PrimaryCardMatched,
            controllerItemCard
        );
        return true;
    }

    internal static Card? TryResolveCurrentPrimaryCard(TooltipParentComponent tooltipParent)
    {
        if (tooltipParent == null)
            return null;

        var primaryController = Traverse
            .Create(tooltipParent)
            .Property("CardTooltipController")
            .GetValue<CardTooltipController>();

        return primaryController?.CurrentCard;
    }

    private static CardController? TryResolveCardController(Card tooltipCard, Card? currentCard)
    {
        var lookup = Data.CardAndSkillLookup;
        if (lookup == null || tooltipCard == null)
            return null;

        var candidates = new List<Card>(2);
        candidates.Add(tooltipCard);
        if (currentCard != null && !ReferenceEquals(currentCard, tooltipCard))
            candidates.Add(currentCard);

        foreach (var candidate in candidates)
        {
            var directMatch = lookup.GetCardController(candidate);
            if (directMatch != null)
                return directMatch;
        }

        foreach (var entry in lookup.CardControllerDictionary)
        {
            if (TooltipPreviewTargetSelection.AreSameCard(entry.Key, tooltipCard))
                return entry.Value;

            if (
                currentCard != null
                && TooltipPreviewTargetSelection.AreSameCard(entry.Key, currentCard)
            )
                return entry.Value;
        }

        return null;
    }

    internal static void Report(
        TooltipPreviewTargetOutcome outcome,
        TooltipLogReasonCode reasonCode,
        Card? card
    ) =>
        BppLog.DebugEvent(
            TooltipLogEvents.PreviewTargetResolvedOrSkipped,
            () =>
                [
                    TooltipLogEvents.PreviewTargetOutcome.Bind(outcome),
                    TooltipLogEvents.PreviewTargetReasonCode.Bind(reasonCode),
                    TooltipLogEvents.PreviewTargetTemplateId.Bind(card?.TemplateId),
                    TooltipLogEvents.PreviewTargetCardInstanceId.Bind(card?.InstanceId.ToString()),
                ]
        );
}
