#nullable enable

using System;

namespace BazaarPlusPlus.Game.OverlayPanels;

internal sealed class BppOverlayPanelRegistration : IBppOverlayPanel
{
    private readonly Func<bool> _isVisible;
    private readonly Action _close;

    public BppOverlayPanelRegistration(
        string panelId,
        int sortingBand,
        Func<bool> isVisible,
        Action close
    )
    {
        PanelId = string.IsNullOrWhiteSpace(panelId)
            ? throw new ArgumentException("Panel id is required.", nameof(panelId))
            : panelId;
        SortingBand = sortingBand;
        _isVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
        _close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public string PanelId { get; }

    public int SortingBand { get; }

    public bool IsVisible => _isVisible();

    public void Close() => _close();
}
