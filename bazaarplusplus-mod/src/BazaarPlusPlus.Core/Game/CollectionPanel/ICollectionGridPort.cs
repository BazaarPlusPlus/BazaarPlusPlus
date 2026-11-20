#nullable enable
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel;

// An unavailable grid returns null from Publish to prevent normalization write-back.
// Current must be empty before the first successful publish and while unbound so a stale height cannot survive.
public readonly struct CollectionGridProjection
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

public interface ICollectionGridPort
{
    CollectionGridProjection? Publish(
        IReadOnlyList<CollectionCardVm> cards,
        CollectionTabKind activeTab
    );

    CollectionGridProjection Current { get; }
}
