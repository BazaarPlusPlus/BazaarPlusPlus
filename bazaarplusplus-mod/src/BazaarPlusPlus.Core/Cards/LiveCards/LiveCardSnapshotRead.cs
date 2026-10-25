#nullable enable

using BazaarPlusPlus.Core.Cards.ItemBoards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace BazaarPlusPlus.Core.Cards.LiveCards;

/// <summary>
/// Turns a host's <see cref="LiveCardWalk"/> into the <see cref="LiveCardSnapshotSet"/> and
/// reports what it left out, the one owner of the live read's outcome on both hosts (ADR-0017).
/// A board or stash card is kept when it fits its socket under the item boards' slot rule; a
/// misfit is left out and becomes an issue. The fallbacks (Bronze, Small, no enchantment, empty
/// attributes) apply here, <c>Order</c> numbers the kept cards in walk order, and nothing is
/// sorted. A walk or an assembly that throws reads as empty with one read-exception issue. Each
/// issue is written as one warning on the host's own event, which the host builds at its call
/// site. Never throws; call it on the thread the walk may run on.
/// </summary>
public static class LiveCardSnapshotRead
{
    public static LiveCardSnapshotReadOutcome Read(
        Func<LiveCardWalk?> walk,
        BppLogEvent degraded,
        LiveCardSnapshotLogValues values
    )
    {
        LiveCardSnapshotReadOutcome outcome;
        try
        {
            outcome = walk() is { } read
                ? Assemble(read)
                : new LiveCardSnapshotReadOutcome(
                    LiveCardSnapshotSet.Empty,
                    Array.Empty<LiveCardSnapshotIssue>()
                );
        }
        catch (Exception exception)
        {
            outcome = new LiveCardSnapshotReadOutcome(
                LiveCardSnapshotSet.Empty,
                [
                    new LiveCardSnapshotIssue(
                        LiveCardSnapshotSection.All,
                        LiveCardSnapshotFailureReason.ReadException,
                        TemplateId: null,
                        Socket: null,
                        Size: null,
                        exception
                    ),
                ]
            );
        }

        Report(outcome.Issues, degraded, values);
        return outcome;
    }

    private static LiveCardSnapshotReadOutcome Assemble(LiveCardWalk walk)
    {
        var issues = new List<LiveCardSnapshotIssue>();
        var shop = new LiveCardSnapshot[walk.Shop.Count];
        for (var index = 0; index < shop.Length; index++)
            shop[index] = Snapshot(walk.Shop[index], index, socket: null);
        return new LiveCardSnapshotReadOutcome(
            new LiveCardSnapshotSet(
                walk.Hero,
                shop,
                Placed(walk.Board, LiveCardSnapshotSection.Board, issues),
                Placed(walk.Stash, LiveCardSnapshotSection.Stash, issues)
            ),
            issues
        );
    }

    private static IReadOnlyList<LiveCardSnapshot> Placed(
        IReadOnlyList<LiveCardRead> cards,
        LiveCardSnapshotSection section,
        List<LiveCardSnapshotIssue> issues
    )
    {
        var result = new List<LiveCardSnapshot>(cards.Count);
        foreach (var card in cards)
        {
            var size = card.Size ?? CardSize.Small;
            if (
                card.Socket is not { } socket
                || !BppItemBoardSlotPlanner.CanFit(socket, BppItemBoardSpan.Resolve(size))
            )
            {
                issues.Add(
                    new LiveCardSnapshotIssue(
                        section,
                        LiveCardSnapshotFailureReason.InvalidPlacement,
                        card.TemplateId,
                        card.Socket,
                        size,
                        Exception: null
                    )
                );
                continue;
            }

            result.Add(Snapshot(card, result.Count, socket));
        }

        return result;
    }

    private static LiveCardSnapshot Snapshot(LiveCardRead card, int order, int? socket) =>
        new()
        {
            InstanceId = card.InstanceId ?? string.Empty,
            TemplateId = card.TemplateId,
            Order = order,
            Tier = card.Tier ?? CardTier.Bronze,
            Size = card.Size ?? CardSize.Small,
            EnchantmentType = card.Enchantment,
            SocketId = socket,
            Attributes = card.Attributes ?? new Dictionary<string, int>(),
        };

    private static void Report(
        IReadOnlyList<LiveCardSnapshotIssue> issues,
        BppLogEvent degraded,
        LiveCardSnapshotLogValues values
    )
    {
        foreach (var issue in issues)
        {
            var fields = new BppLogField[]
            {
                ("section", issue.Section),
                ("reason_code", issue.Reason),
                ("template_id", issue.TemplateId),
                ("socket_id", SocketValue(issue.Socket, values)),
                ("item_size", SizeValue(issue.Size, values)),
            };
            if (issue.Exception == null)
                BppLog.WarnEvent(degraded, fields);
            else
                BppLog.WarnEvent(degraded, issue.Exception, fields);
        }
    }

    // A socket the facets name renders through the facet; one outside the board renders as its
    // index.
    private static object? SocketValue(int? socket, LiveCardSnapshotLogValues values) =>
        socket switch
        {
            null => null,
            { } index when CardFacetOrder.SocketAt(index) is { } facet => values
            == LiveCardSnapshotLogValues.MemberName
                ? facet.ToString()
                : facet,
            { } index => index,
        };

    private static object? SizeValue(CardSize? size, LiveCardSnapshotLogValues values) =>
        size switch
        {
            null => null,
            { } named => values == LiveCardSnapshotLogValues.MemberName ? named.ToString() : named,
        };
}
