using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Core.Cards.LiveCards;
using Il2CppBazaarGameClient.Domain.Models.Cards;
using Il2CppBazaarGameShared.Domain.Core.Types;
using Il2CppTheBazaar;

namespace BazaarPlusPlus.Android.GameInterop;

/// <summary>
/// The Android counterpart of the desktop's <c>LiveCardSnapshotReader</c>: the run's hero, the
/// shop's item offers, and the items on the board and in the stash, as Core's
/// <see cref="LiveCardSnapshotSet"/> on card facets (ADR-0015, ADR-0017). The shop is
/// <c>Data.CurrentState.SelectionSet</c> in its order, as on the desktop. The board and the stash
/// are read as <see cref="AndroidPvpBattleSnapshots"/> reads the hand, from <c>Data.Entities</c>
/// by owner and unboxed section, never through the container's socket tuples, whose IL2CPP proxies
/// are unreliable. A card that does not fit its socket is left out and reported, as on the
/// desktop. Main thread only.
/// </summary>
internal static class AndroidLiveCardSnapshotReader
{
    private const int SocketCount = 10;

    public static AndroidLiveCardSnapshotOutcome Read()
    {
        var issues = new List<AndroidLiveCardSnapshotIssue>();
        try
        {
            var player = Data.Run?.Player;
            if (player == null)
                return new AndroidLiveCardSnapshotOutcome(LiveCardSnapshotSet.Empty, issues);

            // A hero with no facet reads as no run (ADR-0017).
            CardHero? hero = AndroidCardFacets.Hero.TryToFacet(player.Hero, out var facet)
                ? facet
                : null;
            var board = new List<(int Socket, ItemCard Card)>();
            var stash = new List<(int Socket, ItemCard Card)>();
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
                var label = section == EInventorySection.Hand ? "board" : "stash";
                var socket = AndroidGameIds.Unboxed(() => item.LeftSocketId);
                var size = Size(item);
                if (socket is not { } placed || !CanFitSocket((int)placed, size))
                {
                    issues.Add(
                        new AndroidLiveCardSnapshotIssue(
                            label,
                            "invalid_placement",
                            item.TemplateId.ToString(),
                            socket?.ToString(),
                            size.ToString(),
                            null
                        )
                    );
                    continue;
                }
                (section == EInventorySection.Hand ? board : stash).Add(((int)placed, item));
            }

            return new AndroidLiveCardSnapshotOutcome(
                new LiveCardSnapshotSet(hero, ReadShop(), Ordered(board), Ordered(stash)),
                issues
            );
        }
        catch (Exception exception)
        {
            issues.Add(
                new AndroidLiveCardSnapshotIssue(
                    "all",
                    "read_exception",
                    null,
                    null,
                    null,
                    exception
                )
            );
            return new AndroidLiveCardSnapshotOutcome(LiveCardSnapshotSet.Empty, issues);
        }
    }

    private static IReadOnlyList<LiveCardSnapshot> ReadShop()
    {
        var result = new List<LiveCardSnapshot>();
        var selection = Data.CurrentState?.SelectionSet;
        if (selection == null)
            return result;
        var order = 0;
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
                result.Add(Snapshot(item, order++, socket: null));
        }
        return result;
    }

    private static IReadOnlyList<LiveCardSnapshot> Ordered(List<(int Socket, ItemCard Card)> cards)
    {
        var order = 0;
        return cards
            .OrderBy(entry => entry.Socket)
            .Select(entry => Snapshot(entry.Card, order++, entry.Socket))
            .ToArray();
    }

    // A tier or size the facets cannot name falls back the way item boards render it (ADR-0017);
    // an enchantment without a facet reads as none.
    private static LiveCardSnapshot Snapshot(ItemCard card, int order, int? socket)
    {
        var enchantment = AndroidGameIds.Unboxed(() => card.Enchantment);
        var attributes = new Dictionary<string, int>(StringComparer.Ordinal);
        if (card.Attributes != null)
            foreach (var entry in card.Attributes)
                attributes[entry.Key.ToString()] = entry.Value;
        return new LiveCardSnapshot
        {
            InstanceId = card.InstanceId.ToString(),
            TemplateId = Guid.TryParse(card.TemplateId.ToString(), out var id) ? id : Guid.Empty,
            Order = order,
            Tier = AndroidCardFacets.Tier.TryToFacet(card.Tier, out var tier)
                ? tier
                : CardTier.Bronze,
            Size = Size(card),
            EnchantmentType =
                enchantment is { } native
                && AndroidCardFacets.Enchantment.TryToFacet(native, out var facet)
                    ? facet
                    : null,
            SocketId = socket,
            Attributes = attributes,
        };
    }

    private static CardSize Size(ItemCard card) =>
        AndroidCardFacets.Size.TryToFacet(card.Size, out var size) ? size : CardSize.Small;

    private static bool CanFitSocket(int socket, CardSize size) =>
        socket >= 0 && socket + BppItemBoardSpan.Resolve(size) <= SocketCount;
}

/// <summary>A live card the reader left out, or a read that failed.</summary>
internal sealed record AndroidLiveCardSnapshotIssue(
    string Section,
    string ReasonCode,
    string? TemplateId,
    string? SocketId,
    string? ItemSize,
    Exception? Exception
);

internal sealed record AndroidLiveCardSnapshotOutcome(
    LiveCardSnapshotSet Snapshot,
    IReadOnlyList<AndroidLiveCardSnapshotIssue> Issues
);
