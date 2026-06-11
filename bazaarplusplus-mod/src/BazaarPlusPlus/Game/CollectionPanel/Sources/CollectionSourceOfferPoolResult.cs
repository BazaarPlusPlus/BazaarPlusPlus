#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

internal sealed class CollectionSourceOfferPoolResult
{
    private CollectionSourceOfferPoolResult(
        CollectionSourceOfferPoolStatus status,
        IReadOnlyCollection<Guid> offeredCardIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>> offerMatchesByCardId
    )
    {
        Status = status;
        OfferedCardIds = offeredCardIds;
        OfferMatchesByCardId = offerMatchesByCardId;
    }

    public CollectionSourceOfferPoolStatus Status { get; }

    public IReadOnlyCollection<Guid> OfferedCardIds { get; }

    public IReadOnlyDictionary<
        Guid,
        IReadOnlyList<CollectionSourceOfferMatch>
    > OfferMatchesByCardId { get; }

    public static CollectionSourceOfferPoolResult NoneSelected() =>
        new(
            CollectionSourceOfferPoolStatus.NoneSelected,
            Array.Empty<Guid>(),
            new Dictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>()
        );

    public static CollectionSourceOfferPoolResult Ready(
        IReadOnlyCollection<Guid> offeredCardIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>? offerMatchesByCardId
    ) =>
        new(
            CollectionSourceOfferPoolStatus.Ready,
            offeredCardIds ?? Array.Empty<Guid>(),
            offerMatchesByCardId
                ?? new Dictionary<Guid, IReadOnlyList<CollectionSourceOfferMatch>>()
        );
}
