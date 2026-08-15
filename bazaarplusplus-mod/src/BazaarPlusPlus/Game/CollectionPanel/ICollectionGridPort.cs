#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel;

// Three-window grid contract for CollectionViewState:
// - Publish → null when the grid is unavailable (virtualizer missing/disposed); the reducer then
//   keeps the whole early-return semantics: skip normalization write-back.
// - Current → Empty before the first successful Publish and while the adapter is unbound; never a
//   contradictory VisibleCount=0 with a stale ContentHeight.
// - Viewport-driven projection drift is owned by the panel, not this port.
internal readonly struct CollectionGridProjection
{
    public CollectionGridProjection(int visibleCount, float contentHeight)
    {
        VisibleCount = visibleCount;
        ContentHeight = contentHeight;
    }

    public int VisibleCount { get; }

    public float ContentHeight { get; }

    public static CollectionGridProjection Empty { get; } = new(0, 0f);
}

internal interface ICollectionGridPort
{
    CollectionGridProjection? Publish(
        IReadOnlyList<CollectionCardVm> cards,
        CollectionTabKind activeTab
    );

    CollectionGridProjection Current { get; }
}
