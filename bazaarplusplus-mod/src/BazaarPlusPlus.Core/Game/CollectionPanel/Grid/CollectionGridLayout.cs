#nullable enable
using BazaarPlusPlus.Core.Cards;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

public sealed class CollectionGridLayout
{
    private static readonly CollectionGridCell[] EmptyCells = Array.Empty<CollectionGridCell>();
    private static readonly CollectionGridShelf[] EmptyShelves = Array.Empty<CollectionGridShelf>();

    public static readonly CollectionGridLayout Empty = new(
        EmptyCells,
        EmptyShelves,
        CollectionGridConstants.ItemRowSpan,
        CollectionGridConstants.ItemColumns
    );

    private readonly CollectionGridCell[] _cells;
    private readonly CollectionGridShelf[] _shelves;

    private CollectionGridLayout(
        CollectionGridCell[] cells,
        CollectionGridShelf[] shelves,
        int shelfHeightUnits,
        int columns
    )
    {
        _cells = cells;
        _shelves = shelves;
        ShelfHeightUnits = shelfHeightUnits;
        Columns = columns;
    }

    public int Count => _cells.Length;

    public int Columns { get; }
    public int ShelfHeightUnits { get; }
    public int ShelfCount => _shelves.Length;
    public int TotalRowUnits => _shelves.Length * ShelfHeightUnits;

    public CollectionGridShelf ShelfAt(int shelf) => _shelves[shelf];

    public static CollectionGridLayout Build(
        IReadOnlyList<CollectionCardVm> visible,
        CollectionTabKind activeTab
    ) => Build(visible, activeTab, CollectionGridConstants.ColumnsFor(activeTab.CardType()));

    // Keep enough columns to fit a Large item on the touch grid.
    public static CollectionGridLayout Build(
        IReadOnlyList<CollectionCardVm> visible,
        CollectionTabKind activeTab,
        int columns
    )
    {
        var activeType = activeTab.CardType();
        columns = Math.Max(
            activeType == CardType.Skill ? 1 : CollectionGridConstants.ItemLargeSpan,
            columns
        );
        if (visible == null || visible.Count == 0)
            return new CollectionGridLayout(
                EmptyCells,
                EmptyShelves,
                CollectionGridConstants.RowSpanFor(activeType),
                columns
            );

        return activeType == CardType.Skill
            ? BuildSkill(visible, columns)
            : BuildItem(visible, columns);
    }

    private static CollectionGridLayout BuildSkill(
        IReadOnlyList<CollectionCardVm> visible,
        int columns
    )
    {
        var count = visible.Count;
        var cells = new CollectionGridCell[count];
        for (var i = 0; i < count; i++)
            cells[i] = new CollectionGridCell(i % columns, i / columns, 1);

        var shelfCount = (count + columns - 1) / columns;
        var shelves = new CollectionGridShelf[shelfCount];
        for (var s = 0; s < shelfCount; s++)
        {
            var first = s * columns;
            var last = Math.Min(first + columns - 1, count - 1);
            shelves[s] = new CollectionGridShelf(first, last);
        }

        return new CollectionGridLayout(
            cells,
            shelves,
            CollectionGridConstants.SkillRowSpan,
            columns
        );
    }

    private static CollectionGridLayout BuildItem(
        IReadOnlyList<CollectionCardVm> visible,
        int columns
    )
    {
        var count = visible.Count;
        var cells = new CollectionGridCell[count];
        var shelves = new List<CollectionGridShelf>();

        var shelf = 0;
        var cursorCol = 0;
        var shelfFirst = 0;
        for (var i = 0; i < count; i++)
        {
            var span = Math.Min(columns, CollectionGridConstants.ItemWidthSpan(visible[i].Size));
            if (cursorCol > 0 && cursorCol + span > columns)
            {
                shelves.Add(new CollectionGridShelf(shelfFirst, i - 1));
                shelf++;
                cursorCol = 0;
                shelfFirst = i;
            }

            cells[i] = new CollectionGridCell(cursorCol, shelf, span);
            cursorCol += span;
        }
        shelves.Add(new CollectionGridShelf(shelfFirst, count - 1));

        return new CollectionGridLayout(
            cells,
            shelves.ToArray(),
            CollectionGridConstants.ItemRowSpan,
            columns
        );
    }

    public float ShelfPitch(float unit, float gap) => ShelfHeightUnits * (unit + gap);

    public float ContentHeight(float unit, float gap) => TotalRowUnits * (unit + gap);

    // The scroll offset can overshoot packed content during resize until the scroll view clamps it.
    public (int First, int Last) VisibleIndexRange(
        float scrollY,
        float viewportHeight,
        float unit,
        float gap,
        float originY
    )
    {
        if (_shelves.Length == 0)
            return (0, -1);
        var shelfPitch = ShelfPitch(unit, gap);
        var maxShelf = _shelves.Length - 1;
        var firstShelf = Math.Clamp(
            (int)Math.Floor((scrollY - originY) / shelfPitch) - CollectionGridConstants.RowOverscan,
            0,
            maxShelf
        );
        var lastShelf = Math.Clamp(
            (int)Math.Floor((scrollY + viewportHeight - originY) / shelfPitch)
                + CollectionGridConstants.RowOverscan,
            0,
            maxShelf
        );
        return (_shelves[firstShelf].FirstIndex, _shelves[lastShelf].LastIndex);
    }

    public CollectionGridRect ContentRectFor(
        int index,
        float unit,
        float gap,
        float originX,
        float originY
    )
    {
        var cell = _cells[index];
        var step = unit + gap;
        var x = originX + cell.Col * step;
        var y = originY + cell.Shelf * ShelfHeightUnits * step;
        var width = cell.WidthSpan * unit + (cell.WidthSpan - 1) * gap;
        var height = ShelfHeightUnits * unit + (ShelfHeightUnits - 1) * gap;
        return new CollectionGridRect(x, y, width, height);
    }
}
