#nullable enable
using System;
using TheBazaar.UI.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.Game.ItemBoard;

internal sealed class ItemBoardService : IDisposable
{
    private readonly ItemBoardOverlay _overlay = new ItemBoardOverlay();

    public bool IsAlive => _overlay.IsAlive;

    public bool EnsureHost(CardTooltipController? tooltipController)
    {
        return tooltipController != null && _overlay.Ensure(tooltipController);
    }

    public void Render(ItemBoardRenderInput input)
    {
        _overlay.Render(input);
    }

    public bool ShowTemplateSet(
        CardTooltipController? tooltipController,
        ItemBoardTemplateSetRequest request
    )
    {
        if (!EnsureHost(tooltipController))
            return false;

        _overlay.RenderTemplateSet(request);
        return true;
    }

    public bool ShowTemplateSet(ItemBoardTemplateSetRequest request)
    {
        if (!IsAlive)
            return false;

        _overlay.RenderTemplateSet(request);
        return true;
    }

    public void SetAnchoredPosition(Vector2 anchoredPosition)
    {
        _overlay.SetAnchoredPosition(anchoredPosition);
    }

    public void ClearAnchoredPositionOverride()
    {
        _overlay.ClearAnchoredPositionOverride();
    }

    public void Hide(float hideTime = 0f)
    {
        _overlay.Hide(hideTime);
    }

    public void Dispose()
    {
        _overlay.Dispose();
    }
}
