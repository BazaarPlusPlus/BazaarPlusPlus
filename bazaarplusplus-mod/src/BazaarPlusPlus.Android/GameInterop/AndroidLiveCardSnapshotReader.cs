using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.LiveCards;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Android counterpart of the desktop's <c>LiveCardSnapshotReader</c>: the walk of the run's
/// hero, the shop's item offers, and the items on the board and in the stash, on card facets
/// (ADR-0015, ADR-0017), for Core's <see cref="LiveCardSnapshotRead"/>. The shop is
/// <c>Data.CurrentState.SelectionSet</c> in its order, as on the desktop. The board and the stash
/// are read as <see cref="AndroidPvpBattleSnapshots"/> reads the hand, from <c>Data.Entities</c>
/// by owner and unboxed section, never through the container's socket tuples, whose IL2CPP proxies
/// are unreliable, and are put in socket order here because <c>Data.Entities</c> has none. Core's
/// <see cref="LiveCardSnapshotRead"/> places, numbers and reports the cards. Main thread only.
/// </summary>
internal static class AndroidLiveCardSnapshotReader
{
    public static LiveCardWalk? Walk()
    {
        var player = Data.Run?.Player;
        if (player == null)
            return null;

        CardHero? hero = AndroidCardFacets.Hero.TryToFacet(player.Hero, out var facet)
            ? facet
            : null;
        var board = new List<LiveCardRead>();
        var stash = new List<LiveCardRead>();
        foreach (var card in Data.Entities.Values)
        {
            if (card == null || card.Owner?.Pointer != player.Pointer)
                continue;
            var section = AndroidGameIds.Unboxed(() => card.Section);
            if (section is not (EInventorySection.Hand or EInventorySection.Stash))
                continue;
            var item = card.TryCast<ItemCard>();
            if (item == null)
                continue;
            var socket = AndroidGameIds.Unboxed(() => item.LeftSocketId);
            (section == EInventorySection.Hand ? board : stash).Add(
                Card(item, socket is { } placed ? (int)placed : null)
            );
        }

        return new LiveCardWalk(
            hero,
            ReadShop(),
            board.OrderBy(card => card.Socket).ToArray(),
            stash.OrderBy(card => card.Socket).ToArray()
        );
    }

    private static IReadOnlyList<LiveCardRead> ReadShop()
    {
        var result = new List<LiveCardRead>();
        var selection = Data.CurrentState?.SelectionSet;
        if (selection == null)
            return result;
        foreach (var entry in selection)
        {
            if (string.IsNullOrEmpty(entry))
                continue;
            Card? card;
            try
            {
                card = Data.GetCard(entry);
            }
            catch
            {
                continue;
            }
            var item = card?.TryCast<ItemCard>();
            if (item != null)
                result.Add(Card(item, socket: null));
        }
        return result;
    }

    private static LiveCardRead Card(ItemCard card, int? socket)
    {
        var enchantment = AndroidGameIds.Unboxed(() => card.Enchantment);
        var attributes = new Dictionary<string, int>(StringComparer.Ordinal);
        if (card.Attributes != null)
            foreach (var entry in card.Attributes)
                attributes[entry.Key.ToString()] = entry.Value;
        return new LiveCardRead(
            card.InstanceId.ToString(),
            Guid.TryParse(card.TemplateId.ToString(), out var id) ? id : Guid.Empty,
            AndroidCardFacets.Tier.TryToFacet(card.Tier, out var tier) ? tier : null,
            AndroidCardFacets.Size.TryToFacet(card.Size, out var size) ? size : null,
            enchantment is { } native
            && AndroidCardFacets.Enchantment.TryToFacet(native, out var facet)
                ? facet
                : null,
            socket,
            attributes
        );
    }
}
