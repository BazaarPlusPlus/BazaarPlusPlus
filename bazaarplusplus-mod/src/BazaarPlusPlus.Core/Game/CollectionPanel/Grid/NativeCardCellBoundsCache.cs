#nullable enable
namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

public sealed class NativeCardCellBoundsCache
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

    public void InvalidateOnRebind() => Clear();

    // A resize can change the native bounds through Aspect.SetSize.
    public void InvalidateOnScaleDirty() => Clear();

    // Art can finish after the first measure and assigning its material can change RawImage and FrameContainer bounds.
    public void InvalidateOnArtLoaded() => Clear();

    private void Clear()
    {
        _valid = false;
        _bounds = default;
        _aspectRatio = null;
    }
}
