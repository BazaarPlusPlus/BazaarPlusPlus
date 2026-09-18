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

internal sealed record HistoryPage<T>(
    IReadOnlyList<T> Rows,
    HistoryCursor? First,
    HistoryCursor? Last,
    bool HasNewer,
    bool HasOlder
)
{
    internal static HistoryPage<T> Empty { get; } = new(Array.Empty<T>(), null, null, false, false);

    internal int FindIndex(Predicate<T> match)
    {
        for (var i = 0; i < Rows.Count; i++)
            if (match(Rows[i]))
                return i;
        return -1;
    }
}
