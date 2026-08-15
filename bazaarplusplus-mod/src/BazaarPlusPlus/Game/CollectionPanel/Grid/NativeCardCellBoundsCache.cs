#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Per-cell native visual-bounds cache for Collection grid fit. UnityEngine-free so the
// hit/miss policy is table-tested in CollectionGridLayout.Tests without booting the client.
//
// Invalidation hooks (design): re-bind, scale dirty (viewport/base unit), art load complete.
// Scroll-only reposition reads a warm cache and never remeasures.
internal sealed class NativeCardCellBoundsCache
{
    private bool _valid;
    private CardVisualBounds _bounds;
    private float? _aspectRatio;

    public bool IsValid => _valid;

    public bool TryGet(out CardVisualBounds bounds, out float? aspectRatio)
    {
        if (!_valid)
        {
            bounds = default;
            aspectRatio = null;
            return false;
        }

        bounds = _bounds;
        aspectRatio = _aspectRatio;
        return true;
    }

    public void Store(CardVisualBounds bounds, float? aspectRatio)
    {
        _bounds = bounds;
        _aspectRatio = aspectRatio;
        _valid = true;
    }

    // Fresh native session / Show reactivation: measured subtree may differ from any prior value.
    public void InvalidateOnRebind() => Clear();

    // Viewport or base-unit change: re-fit uses a new cellRect; remeasure keeps Aspect SetSize
    // and layout-dependent bounds honest after a resize.
    public void InvalidateOnScaleDirty() => Clear();

    // Item art finishes loading after (or independently of) the first measure; RawImage /
    // FrameContainer bounds can change once material is assigned.
    public void InvalidateOnArtLoaded() => Clear();

    private void Clear()
    {
        _valid = false;
        _bounds = default;
        _aspectRatio = null;
    }
}
