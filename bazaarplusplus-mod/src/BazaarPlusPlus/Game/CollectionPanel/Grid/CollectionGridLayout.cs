#nullable enable
using System;
using System.Collections.Generic;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Span-aware, viewport-independent packing for the fixed per-tab catalog grid. Skills
// pack one square unit each (Columns per row). Items shelf-pack left-to-right by unit width
// (small 1 / medium 2 / large 3), wrapping to the next ItemRowSpan-tall shelf when the current
// row can't fit the next card. The result is pure grid coordinates (col / shelf / span); the
// per-viewport base unit is applied later by ContentRectFor. Pure + UnityEngine-free so it is
// unit-tested in tests/CollectionGridLayout.Tests.
internal sealed class CollectionGridLayout
{
    private static readonly CollectionGridCell[] EmptyCells = Array.Empty<CollectionGridCell>();
    private static readonly CollectionGridShelf[] EmptyShelves = Array.Empty<CollectionGridShelf>();

    public static readonly CollectionGridLayout Empty = new(
        EmptyCells,
        EmptyShelves,
        CollectionGridConstants.ItemRowSpan,
        CollectionGridConstants.ItemColumns,
        CollectionGridConstants.ItemMaxUnitWidth
    );

    private readonly CollectionGridCell[] _cells;
    private readonly CollectionGridShelf[] _shelves;

    private CollectionGridLayout(
        CollectionGridCell[] cells,
        CollectionGridShelf[] shelves,
        int shelfHeightUnits,
        int columns,
        float maxUnitWidth
    )
    {
        _cells = cells;
        _shelves = shelves;
        ShelfHeightUnits = shelfHeightUnits;
        Columns = columns;
        MaxUnitWidth = maxUnitWidth;
    }

    public int Count => _cells.Length;

    // Per-tab geometry carried on the layout so the virtualizer pixelizes with the right column
    // count and size cap without re-deriving the active type.
    public int Columns { get; }
    public float MaxUnitWidth { get; }
    public int ShelfHeightUnits { get; }
    public int ShelfCount => _shelves.Length;
    public int TotalRowUnits => _shelves.Length * ShelfHeightUnits;

    public CollectionGridCell CellAt(int index) => _cells[index];

    public CollectionGridShelf ShelfAt(int shelf) => _shelves[shelf];

    public static CollectionGridLayout Build(
        IReadOnlyList<CollectionCardVm> visible,
        ECardType activeType
    )
    {
        if (visible == null || visible.Count == 0)
            return new CollectionGridLayout(
                EmptyCells,
                EmptyShelves,
                CollectionGridConstants.RowSpanFor(activeType),
                CollectionGridConstants.ColumnsFor(activeType),
                CollectionGridConstants.MaxUnitWidthFor(activeType)
            );

        return activeType == ECardType.Skill ? BuildSkill(visible) : BuildItem(visible);
    }

    private static CollectionGridLayout BuildSkill(IReadOnlyList<CollectionCardVm> visible)
    {
        var columns = CollectionGridConstants.SkillColumns;
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
            columns,
            CollectionGridConstants.SkillMaxUnitWidth
        );
    }

    private static CollectionGridLayout BuildItem(IReadOnlyList<CollectionCardVm> visible)
    {
        var columns = CollectionGridConstants.ItemColumns;
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
            columns,
            CollectionGridConstants.ItemMaxUnitWidth
        );
    }

    // --- Pixelization (pure; depends only on the per-viewport base unit + gap + grid origin) ---

    public float ShelfPitch(float unit, float gap) => ShelfHeightUnits * (unit + gap);

    public float ContentHeight(float unit, float gap) => TotalRowUnits * (unit + gap);

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
