#nullable enable
namespace BazaarPlusPlus.BazaarAgent;

/// <summary>
/// Pure static helper that enumerates legal item placements within a section.
/// No game-type dependencies — fully testable in isolation.
/// </summary>
public static class BazaarAgentMoveTargetPlanner
{
    /// <summary>
    /// Given an item of size <paramref name="itemSize"/> and a target section with
    /// <paramref name="capacity"/> sockets, where <paramref name="occupiedSockets"/> is the
    /// set of currently-occupied socket indices, returns all legal starting positions as
    /// <see cref="IReadOnlyList{T}"/> of socket-id string lists
    /// ("Socket_0", "Socket_1", …) the item would occupy. Sorted by starting index.
    /// </summary>
    /// <param name="itemSize">Number of consecutive sockets occupied by the item.</param>
    /// <param name="capacity">Total socket capacity of the target section.</param>
    /// <param name="occupiedSockets">Socket indices currently occupied in the target section.</param>
    /// <param name="excludeStartIndexInclusive">
    /// Optional: first socket index to treat as vacant (for "moving from-self" case).
    /// Pass -1 to disable.
    /// </param>
    /// <param name="excludeCountInclusive">Number of consecutive sockets to exclude from <paramref name="excludeStartIndexInclusive"/>.</param>
    public static IReadOnlyList<IReadOnlyList<string>> Enumerate(
        int itemSize,
        int capacity,
        ISet<int> occupiedSockets,
        int excludeStartIndexInclusive = -1,
        int excludeCountInclusive = 0
    )
    {
        var results = new List<IReadOnlyList<string>>();

        if (itemSize <= 0 || itemSize > capacity)
            return results;

        for (int start = 0; start <= capacity - itemSize; start++)
        {
            bool fits = true;
            for (int offset = 0; offset < itemSize; offset++)
            {
                int idx = start + offset;
                // Treat excluded range as vacant
                bool isExcluded =
                    excludeStartIndexInclusive >= 0
                    && idx >= excludeStartIndexInclusive
                    && idx < excludeStartIndexInclusive + excludeCountInclusive;

                if (!isExcluded && occupiedSockets.Contains(idx))
                {
                    fits = false;
                    break;
                }
            }

            if (fits)
            {
                var sockets = new List<string>(itemSize);
                for (int offset = 0; offset < itemSize; offset++)
                    sockets.Add($"Socket_{start + offset}");
                results.Add(sockets);
            }
        }

        return results;
    }
}
