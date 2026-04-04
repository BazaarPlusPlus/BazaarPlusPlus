using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;

namespace BazaarPlusPlus.Game.ItemEnchantPreview;

public static class ItemEnchantPreviewEligibility
{
    public static bool IsEligible(Card card, bool isInCombat)
    {
        if (card == null)
            return false;

        if (!IsEligible(card.Type, card.Section, isInCombat))
        {
            return card.Type == ECardType.Item && !isInCombat && IsOpponentBoardItem(card);
        }

        return true;
    }

    public static bool IsEligible(ECardType cardType, EInventorySection? section, bool isInCombat)
    {
        if (isInCombat || cardType != ECardType.Item)
            return false;

        return section == EInventorySection.Hand || section == EInventorySection.Stash;
    }

    private static bool IsOpponentBoardItem(Card card)
    {
        if (card.Owner == null)
            return true;

        return card.Owner == Data.Run?.Opponent && card.Section == EInventorySection.Hand;
    }
}
