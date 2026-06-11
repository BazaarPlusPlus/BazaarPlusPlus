#nullable enable

using System.Collections;
using BazaarPlusPlus.GameInterop.ItemBoardPreview;
using UnityEngine;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Preview;

internal sealed class LiveItemBoardRowPreview
{
    private readonly BppItemBoardPreview _preview;
    private Rect _bounds;
    private bool _hasBounds;

    public LiveItemBoardRowPreview(BppItemBoardId id, int layer, int sortingOrder)
    {
        _preview = new BppItemBoardPreview(
            new ItemBoardPreviewOptions
            {
                Layer = layer,
                SortingOrder = sortingOrder,
                LayoutMode = ItemBoardPreviewLayoutMode.SlotGrid,
                ShowHover = true,
                LogComponent = $"LiveBuildPanel:{id}",
            }
        );
    }

    public bool SetBounds(Rect bounds)
    {
        if (
            _hasBounds
            && Mathf.Approximately(_bounds.x, bounds.x)
            && Mathf.Approximately(_bounds.y, bounds.y)
            && Mathf.Approximately(_bounds.width, bounds.width)
            && Mathf.Approximately(_bounds.height, bounds.height)
        )
        {
            return false;
        }

        _bounds = bounds;
        _hasBounds = true;
        _preview.SetPosition(new Vector2(bounds.x, bounds.y));
        _preview.SetClipSize(new Vector2(bounds.width, bounds.height));
        var scale = Mathf.Min(
            bounds.width / ItemBoardSocketLayout.NativeBoardWidth,
            bounds.height / ItemBoardSocketLayout.NativeBoardHeight
        );
        _preview.SetCardScale(scale);
        return true;
    }

    public IEnumerator Render(BppItemBoard board)
    {
        return _preview.Render(board);
    }

    public void PollHover(Vector2 mousePixels) => _preview.PollHover(mousePixels);

    public void Hide() => _preview.Hide();

    public void Dispose() => _preview.Dispose();
}
