using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

public sealed class TouchCollectionGridPort : ICollectionGridPort
{
    private bool _published;
    private float _viewportWidth;

    public IReadOnlyList<CollectionCardVm> Visible { get; private set; } =
        Array.Empty<CollectionCardVm>();

    public CollectionTabKind Tab { get; private set; } = CollectionTabKind.Items;

    public CollectionGridLayout Layout { get; private set; } = CollectionGridLayout.Empty;

    public int Revision { get; private set; }

    public CollectionTouchLayout? Touch { get; set; }

    public float ViewportWidth
    {
        get => _viewportWidth;
        set
        {
            _viewportWidth = value;
            var columns = Columns(Tab);
            if (columns != Layout.Columns)
            {
                Layout = CollectionGridLayout.Build(Visible, Tab, columns);
                Revision++;
            }
        }
    }

    public CollectionGridPixelization Pixels =>
        CollectionGridPixelization.ForViewport(ViewportWidth, Layout.Columns);

    public float ContentHeight =>
        Layout.Count == 0
            ? 0f
            : Layout.ContentHeight(Pixels.Unit, CollectionGridConstants.GridGap)
                + 2f * CollectionGridConstants.GridOuterPadding;

    public CollectionGridProjection? Publish(
        IReadOnlyList<CollectionCardVm> cards,
        CollectionTabKind activeTab
    )
    {
        Visible = cards ?? Array.Empty<CollectionCardVm>();
        Tab = activeTab;
        Layout = CollectionGridLayout.Build(Visible, activeTab, Columns(activeTab));
        Revision++;
        _published = true;
        return new CollectionGridProjection(Visible.Count, ContentHeight);
    }

    private int Columns(CollectionTabKind tab) =>
        Touch?.GridColumns(tab.CardType(), _viewportWidth)
        ?? CollectionGridConstants.ColumnsFor(tab.CardType());

    public CollectionGridProjection Current =>
        _published
            ? new CollectionGridProjection(Visible.Count, ContentHeight)
            : CollectionGridProjection.Empty;
}
