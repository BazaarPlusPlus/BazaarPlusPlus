#nullable enable
using BazaarPlusPlus.GameInterop.CardPreview;
using TheBazaar.UI;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.MonsterBoardPreview;

internal readonly record struct NativeMonsterBoardItemBounds(Guid TemplateId, Rect Bounds);

internal sealed partial class OwnedMonsterBoardPreview
{
    private const float PointerRegionPadding = .05f;
    private readonly Vector3[] _itemCorners = new Vector3[4];
    private CardPreviewItem? _pointerItem;
    private readonly List<IDisposable> _tooltipLeases = new();

    private void RegisterTooltips()
    {
        foreach (var card in _view!._activeCards)
            if (card != null && card._tooltipData != null)
                _tooltipLeases.Add(NativeCardPreviewHost.RegisterBorrowedHover(card));
        foreach (var skill in _view._activeSkills)
            if (skill != null && skill._tooltipData != null)
                _tooltipLeases.Add(NativeCardPreviewHost.RegisterBorrowedHover(skill));
    }

    internal void VisitItems(Action<NativeMonsterBoardItemBounds> visit)
    {
        if (!_ready || _disposed || _view == null)
            return;
        foreach (var card in _view._activeCards)
            if (card != null && card._cardData != null)
                visit(new(card._cardData.Id, ItemBounds(card)));
    }

    internal bool TryGetCarpetBounds(out Rect bounds)
    {
        bounds = default;
        if (!_ready || _disposed || _view == null)
            return false;
        _view._carpetImage.rectTransform.GetWorldCorners(_carpetCorners);
        bounds = Rect.MinMaxRect(
            _carpetCorners[0].x,
            _carpetCorners[0].y,
            _carpetCorners[2].x,
            _carpetCorners[2].y
        );
        return bounds.width > 1 && bounds.height > 1;
    }

    // The native prefab owns hover events. Track the owner so its tooltip can end on replacement.
    internal Guid? TrackPointer(Vector2? point)
    {
        _pointerItem = null;
        if (point is not { } pointer || !_ready || _disposed || _view == null)
            return null;
        if (!RegionContains(pointer))
            return null;
        foreach (var card in _view._activeCards)
            if (card != null && card._cardData != null && ItemBounds(card).Contains(pointer))
            {
                _pointerItem = card;
                return card._cardData.Id;
            }
        return null;
    }

    // Pad the fitted carpet bounds so overhanging card frames remain selectable.
    private bool RegionContains(Vector2 point)
    {
        _region.GetWorldCorners(_itemCorners);
        var bounds = WorldBounds(_itemCorners);
        var pad = Mathf.Max(bounds.width, bounds.height) * PointerRegionPadding;
        return point.x >= bounds.xMin - pad
            && point.x <= bounds.xMax + pad
            && point.y >= bounds.yMin - pad
            && point.y <= bounds.yMax + pad;
    }

    private Rect ItemBounds(CardPreviewItem card)
    {
        var frame =
            card._currentFrame != null ? card._currentFrame.transform as RectTransform : null;
        var rect = frame != null ? frame : card.ParentRect;
        rect.GetWorldCorners(_itemCorners);
        return WorldBounds(_itemCorners);
    }

    private static Rect WorldBounds(Vector3[] corners)
    {
        var min = (Vector2)corners[0];
        var max = min;
        for (var i = 1; i < corners.Length; i++)
        {
            min = Vector2.Min(min, corners[i]);
            max = Vector2.Max(max, corners[i]);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private void EndPointer()
    {
        foreach (var lease in _tooltipLeases)
            lease.Dispose();
        _tooltipLeases.Clear();
        var hovered = _pointerItem;
        _pointerItem = null;
        if (hovered != null)
            hovered.OnHoverOut();
    }
}
