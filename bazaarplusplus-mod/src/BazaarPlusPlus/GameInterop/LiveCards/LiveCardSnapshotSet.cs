#nullable enable

using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;

namespace BazaarPlusPlus.GameInterop.LiveCards;

internal sealed class LiveCardSnapshotSet
{
    public static readonly LiveCardSnapshotSet Empty = new(
        null,
        Array.Empty<LiveCardSnapshot>(),
        Array.Empty<LiveCardSnapshot>(),
        Array.Empty<LiveCardSnapshot>()
    );

    public LiveCardSnapshotSet(
        EHero? hero,
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

    public EHero? Hero { get; }

    public IReadOnlyList<LiveCardSnapshot> ShopItems { get; }

    public IReadOnlyList<LiveCardSnapshot> BoardItems { get; }

    public IReadOnlyList<LiveCardSnapshot> StashItems { get; }
}
