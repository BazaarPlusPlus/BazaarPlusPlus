using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.Infrastructure;
using BazaarPlusPlus.Infrastructure.Logging;

namespace LiveBuildPanel.Tests;

/// <summary>
/// Core's live read outcome through its one entry point: the no-run rule, the read-exception
/// envelope, the socket-fit rule, the fallbacks, the numbering, and each host's degraded line.
/// </summary>
internal static class LiveCardSnapshotReadTests
{
    private const string DesktopEventId = "live_build_panel.live_snapshot.degraded";
    private const string TouchEventId = "touch.live_build.live_snapshot.degraded";

    private static readonly Guid CardA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CardB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CardC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid CardD = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    public static void Run()
    {
        NoRunReadsEmptyAndLogsNothing();
        AThrowingWalkReadsEmptyWithOneReadException();
        MisfitsAreLeftOutAndOrderSkipsThem();
        DegradedLinesKeepEachHostsFormat();
        UnnamedFacetsFallBack();
        ShopIgnoresSocketsAndKeepsOrder();
        ANullHeroKeepsTheCards();
        SocketAtInvertsSocketRank();
    }

    private static void NoRunReadsEmptyAndLogsNothing()
    {
        var lines = Install();

        var outcome = LiveCardSnapshotRead.Read(
            () => null,
            DesktopEvent(),
            LiveCardSnapshotLogValues.SnakeCase
        );

        Check.That(
            ReferenceEquals(outcome.Snapshot, LiveCardSnapshotSet.Empty),
            "No run reads as the empty set."
        );
        Check.Equal(0, outcome.Issues.Count, "No run has no issues.");
        Check.Equal(0, Degraded(lines, DesktopEventId).Count, "No run logs nothing.");
    }

    private static void AThrowingWalkReadsEmptyWithOneReadException()
    {
        var lines = Install();
        var failure = new InvalidOperationException("walk failed");

        var outcome = LiveCardSnapshotRead.Read(
            () => throw failure,
            DesktopEvent(),
            LiveCardSnapshotLogValues.SnakeCase
        );

        Check.That(
            ReferenceEquals(outcome.Snapshot, LiveCardSnapshotSet.Empty),
            "A throwing walk reads as the empty set."
        );
        Check.Equal(1, outcome.Issues.Count, "A throwing walk is one issue.");
        Check.Equal(
            new LiveCardSnapshotIssue(
                LiveCardSnapshotSection.All,
                LiveCardSnapshotFailureReason.ReadException,
                null,
                null,
                null,
                failure
            ),
            outcome.Issues[0],
            "A throwing walk is one read exception carrying the exception."
        );
        var line = Degraded(lines, DesktopEventId).Single();
        Check.That(
            line.StartsWith(
                "[BPP][LiveBuildPanel] event=live_build_panel.live_snapshot.degraded section=all reason_code=read_exception template_id=null socket_id=null item_size=null exception_type=",
                StringComparison.Ordinal
            ),
            $"The read exception keeps its fields and exception: {line}"
        );
    }

    private static void MisfitsAreLeftOutAndOrderSkipsThem()
    {
        Install();

        var outcome = LiveCardSnapshotRead.Read(
            () =>
                Walk(
                    board:
                    [
                        Card(CardA, socket: 8, size: CardSize.Large),
                        Card(CardB, socket: 9, size: null),
                        Card(CardC, socket: null, size: CardSize.Small),
                        Card(CardD, socket: 2, size: CardSize.Small),
                    ]
                ),
            DesktopEvent(),
            LiveCardSnapshotLogValues.SnakeCase
        );

        var board = outcome.Snapshot.BoardItems;
        Check.Equal(2, board.Count, "Only the cards that fit stay on the board.");
        Check.Equal(CardB, board[0].TemplateId, "Kept cards stay in walk order, unsorted.");
        Check.Equal(0, board[0].Order, "The first kept card is order 0.");
        Check.Equal(9, board[0].SocketId, "A kept card keeps its socket.");
        Check.Equal(CardSize.Small, board[0].Size, "An unnamed size is Small.");
        Check.Equal(CardTier.Silver, board[0].Tier, "A named tier passes through.");
        Check.Equal(CardD, board[1].TemplateId, "The later fit follows in walk order.");
        Check.Equal(1, board[1].Order, "Order skips the misfits.");
        Check.Equal(2, board[1].SocketId, "The later fit keeps its socket.");
        Check.That(
            outcome.Issues.SequenceEqual([
                new LiveCardSnapshotIssue(
                    LiveCardSnapshotSection.Board,
                    LiveCardSnapshotFailureReason.InvalidPlacement,
                    CardA,
                    8,
                    CardSize.Large,
                    null
                ),
                new LiveCardSnapshotIssue(
                    LiveCardSnapshotSection.Board,
                    LiveCardSnapshotFailureReason.InvalidPlacement,
                    CardC,
                    null,
                    CardSize.Small,
                    null
                ),
            ]),
            $"Each misfit is one issue in walk order: {string.Join(", ", outcome.Issues)}"
        );
    }

    private static void DegradedLinesKeepEachHostsFormat()
    {
        var walk = Walk(
            board: [Card(CardA, socket: 8, size: CardSize.Large)],
            stash: [Card(CardB, socket: 12, size: CardSize.Medium)]
        );

        var desktop = Install();
        LiveCardSnapshotRead.Read(() => walk, DesktopEvent(), LiveCardSnapshotLogValues.SnakeCase);
        Check.That(
            Degraded(desktop, DesktopEventId)
                .SequenceEqual([
                    "[BPP][LiveBuildPanel] event=live_build_panel.live_snapshot.degraded section=board reason_code=invalid_placement template_id=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa socket_id=socket_8 item_size=large",
                    "[BPP][LiveBuildPanel] event=live_build_panel.live_snapshot.degraded section=stash reason_code=invalid_placement template_id=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb socket_id=12 item_size=medium",
                ]),
            $"The desktop lines keep their snake-case values: {string.Join(" | ", desktop)}"
        );

        var touch = Install();
        LiveCardSnapshotRead.Read(() => walk, TouchEvent(), LiveCardSnapshotLogValues.MemberName);
        Check.That(
            Degraded(touch, TouchEventId)
                .SequenceEqual([
                    "[BPP][Touch] event=touch.live_build.live_snapshot.degraded section=board reason_code=invalid_placement template_id=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa socket_id=Socket_8 item_size=Large",
                    "[BPP][Touch] event=touch.live_build.live_snapshot.degraded section=stash reason_code=invalid_placement template_id=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb socket_id=12 item_size=Medium",
                ]),
            $"The Mobile UI lines keep their member-name values: {string.Join(" | ", touch)}"
        );
    }

    private static void UnnamedFacetsFallBack()
    {
        Install();

        var outcome = LiveCardSnapshotRead.Read(
            () =>
                Walk(
                    board:
                    [
                        new LiveCardRead(
                            InstanceId: null,
                            CardA,
                            Tier: null,
                            Size: null,
                            Enchantment: null,
                            Socket: 9,
                            Attributes: null
                        ),
                    ]
                ),
            DesktopEvent(),
            LiveCardSnapshotLogValues.SnakeCase
        );

        var card = outcome.Snapshot.BoardItems.Single();
        Check.Equal(0, outcome.Issues.Count, "An unnamed size fits the last socket.");
        Check.Equal(CardTier.Bronze, card.Tier, "An unnamed tier is Bronze.");
        Check.Equal(CardSize.Small, card.Size, "An unnamed size is Small.");
        Check.Equal<CardEnchantment?>(null, card.EnchantmentType, "No enchantment stays none.");
        Check.Equal(0, card.Attributes.Count, "No attributes read as empty.");
        Check.Equal(string.Empty, card.InstanceId, "No instance id reads as empty.");
    }

    private static void ShopIgnoresSocketsAndKeepsOrder()
    {
        Install();

        var outcome = LiveCardSnapshotRead.Read(
            () =>
                new LiveCardWalk(
                    CardHero.Vanessa,
                    [
                        Card(CardA, socket: 99, size: CardSize.Large),
                        Card(CardB, socket: null, size: CardSize.Small),
                    ],
                    [],
                    []
                ),
            DesktopEvent(),
            LiveCardSnapshotLogValues.SnakeCase
        );

        var shop = outcome.Snapshot.ShopItems;
        Check.Equal(2, shop.Count, "Shop cards are never fit-checked.");
        Check.Equal(CardA, shop[0].TemplateId, "The shop keeps offer order.");
        Check.Equal(0, shop[0].Order, "The first offer is order 0.");
        Check.Equal(1, shop[1].Order, "The second offer is order 1.");
        Check.That(shop.All(card => card.SocketId == null), "A shop card never carries a socket.");
        Check.Equal(0, outcome.Issues.Count, "The shop reports nothing.");
    }

    private static void ANullHeroKeepsTheCards()
    {
        Install();

        var outcome = LiveCardSnapshotRead.Read(
            () => new LiveCardWalk(null, [], [Card(CardA, socket: 0, size: CardSize.Medium)], []),
            DesktopEvent(),
            LiveCardSnapshotLogValues.SnakeCase
        );

        Check.Equal<CardHero?>(null, outcome.Snapshot.Hero, "A hero with no facet stays null.");
        Check.Equal(1, outcome.Snapshot.BoardItems.Count, "A null hero keeps the board.");
    }

    private static void SocketAtInvertsSocketRank()
    {
        for (var index = 0; index < 10; index++)
            Check.Equal(
                index,
                CardFacetOrder.SocketRank(CardFacetOrder.SocketAt(index)!.Value),
                $"SocketAt({index}) inverts SocketRank."
            );
        Check.Equal<CardSocket?>(null, CardFacetOrder.SocketAt(10), "Index 10 is off the board.");
        Check.Equal<CardSocket?>(null, CardFacetOrder.SocketAt(-1), "Index -1 is off the board.");
    }

    private static LiveCardWalk Walk(
        IReadOnlyList<LiveCardRead>? board = null,
        IReadOnlyList<LiveCardRead>? stash = null
    ) => new(CardHero.Vanessa, [], board ?? [], stash ?? []);

    private static LiveCardRead Card(Guid templateId, int? socket, CardSize? size) =>
        new(
            templateId.ToString(),
            templateId,
            CardTier.Silver,
            size,
            Enchantment: null,
            socket,
            new Dictionary<string, int>()
        );

    // The hosts' own events: the pinned lines are the production lines.
    private static BppLogEvent DesktopEvent() =>
        new(BppLogFeatureScope.LiveBuildPanel, DesktopEventId, storm: ["section", "reason_code"]);

    private static BppLogEvent TouchEvent() =>
        new(BppLogFeatureScope.Touch, TouchEventId, storm: ["section", "reason_code"]);

    // A fresh pipeline per test resets the storm window; the previous pipeline's flush can still
    // write its storm summaries (source_event=...) here, so lines are filtered by their own event
    // id.
    private static List<string> Install()
    {
        var lines = new List<string>();
        BppLog.Install(new ListLogSink(lines));
        return lines;
    }

    private static List<string> Degraded(List<string> lines, string eventId) =>
        lines
            .Where(line => line.Contains($"] event={eventId} ", StringComparison.Ordinal))
            .ToList();
}
