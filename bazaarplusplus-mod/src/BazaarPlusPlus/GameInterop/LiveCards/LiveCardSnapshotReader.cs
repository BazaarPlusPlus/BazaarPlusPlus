#nullable enable

using BazaarGameClient.Domain.Cards;
using BazaarGameClient.Domain.Models;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.GameInterop.Cards;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.LiveCards;

/// <summary>
/// The desktop's walk of the run for Core's <see cref="LiveCardSnapshotRead"/>: the hero, the
/// shop's item offers in <c>SelectionSet</c> order, and every item on the board and in the stash
/// in <c>GetCardsAndSockets</c> order, on card facets (ADR-0015). Core places, numbers, and
/// reports the cards. Main thread only.
/// </summary>
internal static class LiveCardSnapshotReader
{
    public static LiveCardWalk? Walk()
    {
        var player = Data.Run?.Player;
        if (player == null)
            return null;

        return new LiveCardWalk(
            DesktopCardFacets.Hero.TryToFacet(player.Hero, out var hero) ? hero : null,
            ReadShopItems(Data.CurrentState as RunState),
            ReadContainerItems(player.Hand),
            ReadContainerItems(player.Stash)
        );
    }

    private static IReadOnlyList<LiveCardRead> ReadShopItems(RunState? runState)
    {
        var result = new List<LiveCardRead>();
        if (runState?.SelectionSet == null)
            return result;

        foreach (var entry in runState.SelectionSet)
        {
            var instanceId = InstanceId.TryParse(entry);
            if (!Data.Entities.TryGetValue(instanceId, out var card))
                continue;
            if (card is not ItemCard itemCard)
                continue;

            result.Add(Card(itemCard, socket: null));
        }

        return result;
    }

    private static IReadOnlyList<LiveCardRead> ReadContainerItems(object? inventory)
    {
        var container = (inventory as CardContainer)?.Container;
        if (container == null)
            return Array.Empty<LiveCardRead>();

        var result = new List<LiveCardRead>();
        foreach (var (socketable, socketId) in container.GetCardsAndSockets())
        {
            if (socketable is ItemCard itemCard)
                result.Add(Card(itemCard, (int)socketId));
        }

        return result;
    }

    private static LiveCardRead Card(ItemCard card, int? socket) =>
        new(
            card.InstanceId.Value,
            card.TemplateId,
            DesktopCardFacets.Tier.TryToFacet(card.Tier, out var tier) ? tier : null,
            DesktopCardFacets.CapturedSize(card.Size),
            DesktopCardFacets.CapturedEnchantment(card.Enchantment),
            socket,
            card.Attributes?.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value)
        );
}
