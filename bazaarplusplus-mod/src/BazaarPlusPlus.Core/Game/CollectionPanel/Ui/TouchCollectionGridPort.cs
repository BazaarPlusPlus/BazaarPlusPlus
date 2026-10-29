using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.CollectionPanel.Grid;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

/// <summary>
/// The grid side of <see cref="CollectionViewState"/> on a touch screen, shared by the Android
/// host and the Mono host's touch Collection: it keeps the visible cards and
/// their Core <see cref="CollectionGridLayout"/>, packed into the column count the touch layout
/// chooses for the grid viewport's width (<see cref="CollectionTouchLayout.GridColumns"/>), and
/// reports the count and content height the view state renders. A new width that changes the
/// column count re-packs the same cards.
/// </summary>
public sealed class TouchCollectionGridPort : ICollectionGridPort
{
    private bool _published;
    private float _viewportWidth;

    public IReadOnlyList<CollectionCardVm> Visible { get; private set; } =
        Array.Empty<CollectionCardVm>();

    public CollectionTabKind Tab { get; private set; } = CollectionTabKind.Items;

    public CollectionGridLayout Layout { get; private set; } = CollectionGridLayout.Empty;

    /// <summary>Bumped on every publish, so the grid knows to re-place its cards.</summary>
    public int Revision { get; private set; }

    /// <summary>The touch layout the columns follow; set before the grid measures its viewport.</summary>
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
