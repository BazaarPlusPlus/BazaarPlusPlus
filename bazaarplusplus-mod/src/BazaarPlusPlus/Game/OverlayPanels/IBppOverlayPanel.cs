#nullable enable

namespace BazaarPlusPlus.Game.OverlayPanels;

internal interface IBppOverlayPanel
{
    string PanelId { get; }

    int SortingBand { get; }

    bool IsVisible { get; }

    void Close();
}
