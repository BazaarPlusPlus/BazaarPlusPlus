#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

/// <summary>
/// The player's current item context, used purely to rank ten-win build matches against what
/// they already own. Board cards weigh the most, stash less, shop the least. Live state never
/// drives recall — recall is keyed on the manually selected candidate cards.
/// </summary>
internal sealed class BuildLiveState
{
    public const int BoardWeight = 3;
    public const int StashWeight = 2;
    public const int ShopWeight = 1;

    public static readonly BuildLiveState Empty = new(
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>()
    );

    private readonly HashSet<Guid> _board;
    private readonly HashSet<Guid> _stash;
    private readonly HashSet<Guid> _shop;

    public BuildLiveState(
        IReadOnlyCollection<Guid>? board,
        IReadOnlyCollection<Guid>? stash,
        IReadOnlyCollection<Guid>? shop
    )
    {
        _board = ToSet(board);
        _stash = ToSet(stash);
        _shop = ToSet(shop);
    }

    public static BuildLiveState From(
        IReadOnlyCollection<Guid>? board,
        IReadOnlyCollection<Guid>? stash,
        IReadOnlyCollection<Guid>? shop
    ) => new(board, stash, shop);

    /// <summary>
    /// Highest applicable zone weight for a card (board &gt; stash &gt; shop), or zero when the card
    /// is not currently live. A card is counted once even if it appears in multiple zones.
    /// </summary>
    public int WeightFor(Guid templateId)
    {
        if (templateId == Guid.Empty)
            return 0;
        if (_board.Contains(templateId))
            return BoardWeight;
        if (_stash.Contains(templateId))
            return StashWeight;
        if (_shop.Contains(templateId))
            return ShopWeight;
        return 0;
    }

    private static HashSet<Guid> ToSet(IReadOnlyCollection<Guid>? ids) =>
        ids == null ? new HashSet<Guid>() : new HashSet<Guid>(ids.Where(id => id != Guid.Empty));
}
