#nullable enable
using BazaarGameClient.Domain.Models.Cards;
using TheBazaar.Tooltips;

namespace BazaarPlusPlus.Game.Tooltips;

internal static class TooltipPreviewTargetSelection
{
    internal static bool AreSameCard(Card? left, Card? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left == null || right == null)
            return false;

        return left.InstanceId == right.InstanceId;
    }

    internal static CardTooltipData? ResolveCurrentPrimaryItemTooltipData(
        ITooltipData? currentTooltipData,
        Card? currentCard
    )
    {
        if (currentTooltipData is not CardTooltipData { CardInstance: ItemCard } cardTooltipData)
            return null;

        if (
            currentCard is ItemCard itemCard
            && !AreSameCard(cardTooltipData.CardInstance, itemCard)
        )
            return null;

        return cardTooltipData;
    }

    internal static bool ShouldAllowImplicitUpgradeSchedule(
        Card? controllerCard,
        bool isCursorOverCard,
        bool isHovering,
        Card? activePrimaryCard
    )
    {
        if (controllerCard == null)
            return false;

        if (activePrimaryCard != null)
            return AreSameCard(controllerCard, activePrimaryCard);

        return isCursorOverCard || isHovering;
    }
}
