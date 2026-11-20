#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Data;

public sealed class CollectionSearchModeState
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
