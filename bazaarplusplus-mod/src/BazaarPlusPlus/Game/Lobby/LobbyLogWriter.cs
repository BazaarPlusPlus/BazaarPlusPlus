#nullable enable
using BazaarGameShared;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.Lobby;

internal static class LobbyLogWriter
{
    internal static void ReportHeroPoolDegraded(
        HeroPoolOperation operation,
        LobbyLogReasonCode reasonCode,
        Exception? exception = null
    )
    {
        var fields = new[]
        {
            LobbyLogEvents.HeroPoolDegradedOperation.Bind(operation),
            LobbyLogEvents.HeroPoolDegradedReasonCode.Bind(reasonCode),
        };
        if (exception == null)
            BppLog.WarnEvent(LobbyLogEvents.HeroPoolDegraded, fields);
        else
            BppLog.WarnEvent(LobbyLogEvents.HeroPoolDegraded, exception, fields);
    }

    internal static void ReportCollectiblePoolDegraded(
        CollectiblePoolOperation operation,
        CollectiblePoolKind collectionKind,
        Exception exception
    ) =>
        BppLog.WarnEvent(
            LobbyLogEvents.CollectiblePoolDegraded,
            exception,
            LobbyLogEvents.CollectiblePoolDegradedOperation.Bind(operation),
            LobbyLogEvents.CollectiblePoolDegradedCollectionKind.Bind(collectionKind),
            LobbyLogEvents.CollectiblePoolDegradedReasonCode.Bind(
                LobbyLogReasonCode.OperationException
            )
        );

    internal static CollectiblePoolKind CollectionKind(
        BazaarInventoryTypes.ECollectionType collectionType
    ) =>
        collectionType switch
        {
            BazaarInventoryTypes.ECollectionType.HeroSkins => CollectiblePoolKind.HeroSkins,
            BazaarInventoryTypes.ECollectionType.Toys => CollectiblePoolKind.Toys,
            BazaarInventoryTypes.ECollectionType.Boards => CollectiblePoolKind.Boards,
            BazaarInventoryTypes.ECollectionType.Carpets => CollectiblePoolKind.Carpets,
            BazaarInventoryTypes.ECollectionType.CardBacks => CollectiblePoolKind.CardBacks,
            BazaarInventoryTypes.ECollectionType.Album => CollectiblePoolKind.Album,
            BazaarInventoryTypes.ECollectionType.Stash => CollectiblePoolKind.Stash,
            BazaarInventoryTypes.ECollectionType.Bank => CollectiblePoolKind.Bank,
            _ => CollectiblePoolKind.Unknown,
        };
}
