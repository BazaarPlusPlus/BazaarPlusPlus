#nullable enable

using BazaarGameClient.Domain.Cards;
using BazaarGameClient.Domain.Models;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Core.Cards.LiveCards;
using BazaarPlusPlus.GameInterop.Cards;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.LiveCards;

internal sealed class LiveCardSnapshotReader
{
    public LiveCardSnapshotReadOutcome Read()
    {
        var issues = new List<LiveCardSnapshotIssue>();
        try
        {
            var run = Data.Run;
            if (run?.Player == null)
                return new LiveCardSnapshotReadOutcome(LiveCardSnapshotSet.Empty, issues);

            var runState = Data.CurrentState as RunState;
            return new LiveCardSnapshotReadOutcome(
                new LiveCardSnapshotSet(
                    // A hero with no facet reads as no run (ADR-0017).
                    DesktopCardFacets.Hero.TryToFacet(run.Player.Hero, out var hero)
                        ? hero
                        : null,
                    ReadShopItems(runState),
                    ReadContainerItems(run.Player.Hand, LiveCardSnapshotSection.Board, issues),
                    ReadContainerItems(run.Player.Stash, LiveCardSnapshotSection.Stash, issues)
                ),
                issues
            );
        }
        catch (Exception ex)
        {
            issues.Add(
                new LiveCardSnapshotIssue(
                    LiveCardSnapshotSection.All,
                    LiveCardSnapshotFailureReason.ReadException,
                    templateId: null,
                    socketId: null,
                    itemSize: null,
                    ex
                )
            );
            return new LiveCardSnapshotReadOutcome(LiveCardSnapshotSet.Empty, issues);
        }
    }

    private static IReadOnlyList<LiveCardSnapshot> ReadShopItems(RunState? runState)
    {
        var result = new List<LiveCardSnapshot>();
        if (runState?.SelectionSet == null)
            return result;

        var order = 0;
        foreach (var entry in runState.SelectionSet)
        {
            var instanceId = InstanceId.TryParse(entry);
            if (!Data.Entities.TryGetValue(instanceId, out var card))
                continue;
            if (card is not ItemCard itemCard)
                continue;

            result.Add(BuildSnapshot(itemCard, order++, socketId: null));
        }

        return result;
    }

    private static IReadOnlyList<LiveCardSnapshot> ReadContainerItems(
        object? inventory,
        LiveCardSnapshotSection section,
        List<LiveCardSnapshotIssue> issues
    )
    {
        if (inventory == null)
            return Array.Empty<LiveCardSnapshot>();

        var container = (inventory as CardContainer)?.Container;
        if (container == null)
            return Array.Empty<LiveCardSnapshot>();

        var result = new List<LiveCardSnapshot>();
        var order = 0;
        foreach (var (socketable, socketId) in container.GetCardsAndSockets())
        {
            if (socketable is not ItemCard itemCard)
                continue;

            if (!CanFitSocket(itemCard.Size, socketId))
            {
                issues.Add(
                    new LiveCardSnapshotIssue(
                        section,
                        LiveCardSnapshotFailureReason.InvalidPlacement,
                        itemCard.TemplateId,
                        socketId,
                        itemCard.Size,
                        exception: null
                    )
                );
                continue;
            }

            result.Add(BuildSnapshot(itemCard, order++, socketId));
        }

        return result;
    }

    private static LiveCardSnapshot BuildSnapshot(
        ItemCard card,
        int order,
        EContainerSocketId? socketId
    )
    {
        // A tier or size the facets cannot name falls back the way item boards render it
        // (ADR-0017); an enchantment without a facet reads as none.
        return new LiveCardSnapshot
        {
            InstanceId = card.InstanceId.Value ?? string.Empty,
            TemplateId = card.TemplateId,
            Order = order,
            Tier = DesktopCardFacets.Tier.TryToFacet(card.Tier, out var tier)
                ? tier
                : CardTier.Bronze,
            Size = DesktopCardFacets.Size.TryToFacet(card.Size, out var size)
                ? size
                : CardSize.Small,
            EnchantmentType =
                card.Enchantment is { } enchantment
                && DesktopCardFacets.Enchantment.TryToFacet(enchantment, out var facet)
                    ? facet
                    : null,
            SocketId = socketId.HasValue ? (int)socketId.Value : null,
            Attributes =
                card.Attributes != null
                    ? card.Attributes.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value)
                    : new Dictionary<string, int>(),
        };
    }

    private static bool CanFitSocket(ECardSize size, EContainerSocketId socketId)
    {
        var span = CardSizeSpan.Resolve(size);
        return (int)socketId >= 0 && (int)socketId + span <= SocketedContainer.SocketCount;
    }
}

internal enum LiveCardSnapshotSection
{
    All,
    Board,
    Stash,
}

internal enum LiveCardSnapshotFailureReason
{
    ReadException,
    InvalidPlacement,
}

internal sealed class LiveCardSnapshotIssue
{
    internal LiveCardSnapshotIssue(
        LiveCardSnapshotSection section,
        LiveCardSnapshotFailureReason reason,
        Guid? templateId,
        EContainerSocketId? socketId,
        ECardSize? itemSize,
        Exception? exception
    )
    {
        Section = section;
        Reason = reason;
        TemplateId = templateId;
        SocketId = socketId;
        ItemSize = itemSize;
        Exception = exception;
    }

    internal LiveCardSnapshotSection Section { get; }
    internal LiveCardSnapshotFailureReason Reason { get; }
    internal Guid? TemplateId { get; }
    internal EContainerSocketId? SocketId { get; }
    internal ECardSize? ItemSize { get; }
    internal Exception? Exception { get; }
}

internal sealed class LiveCardSnapshotReadOutcome
{
    internal LiveCardSnapshotReadOutcome(
        LiveCardSnapshotSet snapshot,
        IReadOnlyList<LiveCardSnapshotIssue> issues
    )
    {
        Snapshot = snapshot;
        Issues = issues;
    }

    internal LiveCardSnapshotSet Snapshot { get; }
    internal IReadOnlyList<LiveCardSnapshotIssue> Issues { get; }
}
