#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Production adapter over CollectionGridVirtualizer. Bind(null) or Dispose clears the window so
// Current returns Empty rather than a stale ContentHeight with VisibleCount=0.
internal sealed class CollectionGridPortAdapter : ICollectionGridPort
{
    private CollectionGridVirtualizer? _virtualizer;
    private bool _hasPublished;

    public void Bind(CollectionGridVirtualizer? virtualizer)
    {
        _virtualizer = virtualizer;
        _hasPublished = false;
    }

    public CollectionGridProjection? Publish(
        IReadOnlyList<CollectionCardVm> cards,
        CollectionTabKind activeTab
    )
    {
        if (_virtualizer == null)
            return null;

        _virtualizer.SetVisible(cards, activeTab);
        _hasPublished = true;
        return new CollectionGridProjection(_virtualizer.VisibleCount, _virtualizer.ContentHeight);
    }

    public CollectionGridProjection Current
    {
        get
        {
            if (_virtualizer == null || !_hasPublished)
                return CollectionGridProjection.Empty;

            return new CollectionGridProjection(
                _virtualizer.VisibleCount,
                _virtualizer.ContentHeight
            );
        }
    }
}
