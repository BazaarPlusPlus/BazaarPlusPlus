#nullable enable
using System.Collections;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Targeting;

namespace BazaarPlusPlus.GameInterop.LiveCards;

internal static class LiveCardTagReader
{
    public static HashSet<ECardTag>? TryReadSectionTypes(
        ETargetCardSectionTargetSection section,
        Card? excludedCard
    )
    {
        var player = TheBazaar.Data.Run?.Player;
        if (player == null)
            return null;

        var present = new HashSet<ECardTag>();
        if (
            section
            is ETargetCardSectionTargetSection.SelfHand
                or ETargetCardSectionTargetSection.SelfHandAndStash
        )
            AddTags(present, player.Hand?.GetItemsAsEnumerable(), excludedCard);
        if (
            section
            is ETargetCardSectionTargetSection.SelfStash
                or ETargetCardSectionTargetSection.SelfHandAndStash
        )
            AddTags(present, player.Stash?.GetItemsAsEnumerable(), excludedCard);
        return present;
    }

    private static void AddTags(HashSet<ECardTag> present, IEnumerable? cards, Card? excludedCard)
    {
        if (cards == null)
            return;

        foreach (var value in cards)
        {
            if (value is Card card && !ReferenceEquals(card, excludedCard))
                present.UnionWith(card.Tags);
        }
    }
}
