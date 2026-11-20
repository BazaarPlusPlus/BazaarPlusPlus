#nullable enable
namespace BazaarPlusPlus.Game.LiveBuildPanel.Recommendations;

// Live state ranks only. Manually selected template IDs determine which builds are recalled.
public sealed class BuildLiveState
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
