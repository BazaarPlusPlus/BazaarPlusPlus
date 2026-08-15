#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Data;

// Presentation state for the operation-row search overlay. The query itself remains owned by
// CollectionFilterState so the existing debounce and filter engine keep one source of truth.
internal sealed class CollectionSearchModeState
{
    public bool IsExpanded { get; private set; } = true;

    public bool Expand(CollectionFilterState filter)
    {
        IsExpanded = true;
        return ClearQuery(filter);
    }

    public bool Collapse(CollectionFilterState filter)
    {
        IsExpanded = false;
        return ClearQuery(filter);
    }

    public bool Reset(CollectionFilterState filter)
    {
        IsExpanded = true;
        return ClearQuery(filter);
    }

    private static bool ClearQuery(CollectionFilterState filter)
    {
        if (filter == null)
            throw new ArgumentNullException(nameof(filter));
        if (string.IsNullOrEmpty(filter.SearchQuery))
            return false;

        filter.SearchQuery = string.Empty;
        return true;
    }
}
