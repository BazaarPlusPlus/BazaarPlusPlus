#nullable enable
namespace BazaarPlusPlus.Game.HistoryPanel.Storage;

internal readonly record struct HistoryCursor(string Time, string Id);

internal sealed record HistoryPageRequest(
    HistoryCursor? Cursor = null,
    bool Newer = false,
    bool Inclusive = false,
    string? AnchorId = null,
    int Limit = 40
);

// Rows with the time/ID anchors and neighbour flags a pager needs. A page with rows always has
// both anchors; an empty page has neither and no neighbours.
internal class HistoryCursorPage<T>
{
    internal static HistoryCursorPage<T> Empty { get; } = new();

    private protected HistoryCursorPage() => Rows = Array.Empty<T>();

    internal HistoryCursorPage(
        IReadOnlyList<T> rows,
        HistoryCursor first,
        HistoryCursor last,
        bool hasNewer,
        bool hasOlder
    )
    {
        if (rows.Count == 0)
            throw new ArgumentException("An anchored page needs rows.", nameof(rows));
        Rows = rows;
        First = first;
        Last = last;
        HasNewer = hasNewer;
        HasOlder = hasOlder;
    }

    public IReadOnlyList<T> Rows { get; }
    public HistoryCursor? First { get; }
    public HistoryCursor? Last { get; }
    public bool HasNewer { get; }
    public bool HasOlder { get; }

    internal int FindIndex(Predicate<T> match)
    {
        for (var i = 0; i < Rows.Count; i++)
            if (match(Rows[i]))
                return i;
        return -1;
    }
}

// A cursor page that also knows its one-based position and the filtered total. Neighbours follow
// from those counts, so they cannot disagree with them. An empty page has position 0 and may
// still carry a total: a cursor past either end finds no rows in a nonempty population.
internal sealed class HistoryCountedPage<T> : HistoryCursorPage<T>
{
    private HistoryCountedPage(long totalCount)
    {
        if (totalCount < 0)
            throw new ArgumentOutOfRangeException(nameof(totalCount));
        TotalCount = totalCount;
    }

    internal HistoryCountedPage(
        IReadOnlyList<T> rows,
        HistoryCursor first,
        HistoryCursor last,
        long firstPosition,
        long totalCount
    )
        : base(
            rows,
            first,
            last,
            hasNewer: firstPosition > 1,
            hasOlder: firstPosition + rows.Count - 1 < totalCount
        )
    {
        if (firstPosition < 1)
            throw new ArgumentOutOfRangeException(nameof(firstPosition));
        if (totalCount < firstPosition + rows.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(totalCount));
        FirstPosition = firstPosition;
        TotalCount = totalCount;
    }

    internal static new HistoryCountedPage<T> Empty(long totalCount = 0) => new(totalCount);

    public long FirstPosition { get; }
    public long LastPosition => Rows.Count == 0 ? 0 : FirstPosition + Rows.Count - 1;
    public long TotalCount { get; }
}
