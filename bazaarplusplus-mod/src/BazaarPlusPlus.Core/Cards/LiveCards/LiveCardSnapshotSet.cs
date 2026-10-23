#nullable enable

namespace BazaarPlusPlus.Core.Cards.LiveCards;

/// <summary>The live shop, board, and stash of the current run; a null hero means no run.</summary>
public sealed class LiveCardSnapshotSet
{
    public static readonly LiveCardSnapshotSet Empty = new(
        null,
        Array.Empty<LiveCardSnapshot>(),
        Array.Empty<LiveCardSnapshot>(),
        Array.Empty<LiveCardSnapshot>()
    );

    public LiveCardSnapshotSet(
        CardHero? hero,
        IReadOnlyList<LiveCardSnapshot>? shopItems,
        IReadOnlyList<LiveCardSnapshot>? boardItems,
        IReadOnlyList<LiveCardSnapshot>? stashItems
    )
    {
        Hero = hero;
        ShopItems = shopItems ?? Array.Empty<LiveCardSnapshot>();
        BoardItems = boardItems ?? Array.Empty<LiveCardSnapshot>();
        StashItems = stashItems ?? Array.Empty<LiveCardSnapshot>();
    }

    public CardHero? Hero { get; }

    public IReadOnlyList<LiveCardSnapshot> ShopItems { get; }

    public IReadOnlyList<LiveCardSnapshot> BoardItems { get; }

    public IReadOnlyList<LiveCardSnapshot> StashItems { get; }
}
