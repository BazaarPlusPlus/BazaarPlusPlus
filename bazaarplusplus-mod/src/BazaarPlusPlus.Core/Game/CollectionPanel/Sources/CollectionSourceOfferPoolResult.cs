#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Sources;

public sealed class CollectionSourceOfferPoolResult
{
    private CollectionSourceOfferPoolResult(
        CollectionSourceOfferPoolStatus status,
        IReadOnlyCollection<Guid> offeredCardIds
    )
    {
        Status = status;
        OfferedCardIds = offeredCardIds;
    }

    public CollectionSourceOfferPoolStatus Status { get; }

    public IReadOnlyCollection<Guid> OfferedCardIds { get; }

    public static CollectionSourceOfferPoolResult NoneSelected() =>
        new(CollectionSourceOfferPoolStatus.NoneSelected, Array.Empty<Guid>());

    public static CollectionSourceOfferPoolResult Ready(IReadOnlyCollection<Guid> offeredCardIds) =>
        new(CollectionSourceOfferPoolStatus.Ready, offeredCardIds ?? Array.Empty<Guid>());
}
