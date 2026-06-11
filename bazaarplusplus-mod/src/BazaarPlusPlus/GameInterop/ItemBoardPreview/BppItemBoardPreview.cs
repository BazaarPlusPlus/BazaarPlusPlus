#nullable enable

using System;
using System.Collections;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal sealed class BppItemBoardPreview : IDisposable
{
    private readonly ItemBoardPreviewSurface _surface = new();
    private readonly ItemBoardPreviewOptions _options;

    public BppItemBoardPreview(ItemBoardPreviewOptions options)
    {
        _options = options ?? new ItemBoardPreviewOptions();
    }

    public void CancelPending() => _surface.CancelPending();

    public void SetPosition(Vector2 position) => _surface.SetPosition(position);

    public void SetClipSize(Vector2 size) => _surface.SetClipSize(size);

    public bool SetCardScale(float scale) => _surface.SetCardScale(scale);

    public IEnumerator Render(
        BppItemBoard? board,
        Action<ItemBoardPreviewPhase>? onPhase = null,
        Action? onComplete = null
    )
    {
        var plannedBoard = BppItemBoardSlotPlanner.Plan(board);
        return _surface.Render(
            BppItemBoardPreviewMapper.Map(plannedBoard),
            ItemBoardPreviewOptionsForwarder.ForSurface(_options),
            plannedBoard.Signature,
            onPhase,
            onComplete
        );
    }

    public void PollHover(Vector2 mousePixels) => _surface.PollHover(mousePixels);

    public void Hide() => _surface.Hide();

    public void Dispose() => _surface.Dispose();
}
