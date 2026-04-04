#nullable enable
using System.Collections.Generic;
using BazaarGameClient.Domain.Models.Cards;
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
            BppLog.Info("TooltipPreview", "ResolverSkipped reason=no-primary-controller");
            return false;
        }

        var tooltipData = TooltipPreviewTargetSelection.ResolveCurrentPrimaryItemTooltipData(
            primaryController.CurrentTooltipData,
            primaryController.CurrentCard
        );
        if (tooltipData?.CardInstance is not ItemCard itemCard)
        {
            BppLog.Info(
                "TooltipPreview",
                $"ResolverSkipped reason=no-item-tooltip-data primaryCurrentCard={DescribeCard(primaryController.CurrentCard)} primaryTooltipCard={DescribeTooltipDataCard(primaryController.CurrentTooltipData)}"
            );
            return false;
        }

        var cardController = TryResolveCardController(itemCard, primaryController.CurrentCard);
        if (cardController?.CardData is not ItemCard controllerItemCard)
        {
            BppLog.Info(
                "TooltipPreview",
                $"ResolverSkipped reason=no-card-controller tooltipCard={DescribeCard(itemCard)} primaryCurrentCard={DescribeCard(primaryController.CurrentCard)}"
            );
            return false;
        }

        if (!TooltipPreviewTargetSelection.AreSameCard(controllerItemCard, itemCard))
        {
            BppLog.Info(
                "TooltipPreview",
                $"ResolverSkipped reason=controller-card-mismatch tooltipCard={DescribeCard(itemCard)} controllerCard={DescribeCard(controllerItemCard)} tooltipInstance={itemCard.InstanceId} controllerInstance={controllerItemCard.InstanceId}"
            );
            return false;
        }

        target = new TooltipRefreshTarget(cardController, controllerItemCard, tooltipData);
        BppLog.Info(
            "TooltipPreview",
            $"ResolverMatched tooltipCard={DescribeCard(itemCard)} controllerCard={DescribeCard(controllerItemCard)} tooltipInstance={itemCard.InstanceId} controllerInstance={controllerItemCard.InstanceId} primaryCurrentCard={DescribeCard(primaryController.CurrentCard)}"
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

    private static string DescribeTooltipDataCard(ITooltipData? tooltipData)
    {
        return tooltipData is CardTooltipData cardTooltipData
            ? DescribeCard(cardTooltipData.CardInstance)
            : tooltipData?.GetType().Name ?? "null";
    }

    private static string DescribeCard(Card? card)
    {
        if (card == null)
            return "null";

        var templateName = card.Template?.InternalName;
        return !string.IsNullOrWhiteSpace(templateName)
            ? templateName
            : $"{card.TemplateId}:{card.InstanceId}";
    }
}
