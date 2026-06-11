#nullable enable

using System;
using System.Collections.Generic;
using BazaarGameClient.Domain.Cards;
using BazaarGameClient.Domain.Models;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Runs;
using BazaarPlusPlus.GameInterop.Cards;
using BazaarPlusPlus.Infrastructure;
using TheBazaar;

namespace BazaarPlusPlus.GameInterop.LiveCards;

internal sealed class LiveCardSnapshotReader
{
    private const string LogCategory = "LiveCardSnapshotReader";

    public LiveCardSnapshotSet Read()
    {
        try
        {
            var run = Data.Run;
            if (run?.Player == null)
                return LiveCardSnapshotSet.Empty;

            var runState = Data.CurrentState as RunState;
            return new LiveCardSnapshotSet(
                run.Player.Hero,
                ReadShopItems(runState),
                ReadContainerItems(run.Player.Hand, "board"),
                ReadContainerItems(run.Player.Stash, "stash")
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn(LogCategory, $"Live card snapshot read failed: {ex.Message}");
            return LiveCardSnapshotSet.Empty;
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
        string name
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
                BppLog.Warn(
                    LogCategory,
                    $"Skipped {name} item with invalid socket={socketId} size={itemCard.Size} templateId={itemCard.TemplateId}."
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
        return new LiveCardSnapshot
        {
            InstanceId = card.InstanceId.Value ?? string.Empty,
            TemplateId = card.TemplateId,
            Order = order,
            Tier = card.Tier,
            Size = card.Size,
            EnchantmentType = card.Enchantment,
            SocketId = socketId,
            Attributes =
                card.Attributes != null
                    ? new Dictionary<ECardAttributeType, int>(card.Attributes)
                    : new Dictionary<ECardAttributeType, int>(),
        };
    }

    private static bool CanFitSocket(ECardSize size, EContainerSocketId socketId)
    {
        var span = CardSizeSpan.Resolve(size);
        return (int)socketId >= 0 && (int)socketId + span <= SocketedContainer.SocketCount;
    }
}
