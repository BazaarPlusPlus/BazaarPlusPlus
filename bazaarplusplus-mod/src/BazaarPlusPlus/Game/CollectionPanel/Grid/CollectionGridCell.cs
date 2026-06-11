#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// One card's placement on the fixed unit grid, viewport-independent. Col is the left unit
// column [0, Columns); Shelf is the band index (each shelf is ShelfHeightUnits tall); WidthSpan
// is the card's unit width (Skill = 1; Item = 1/2/3 by ECardSize). Pixel rects are derived from
// these by CollectionGridLayout.ContentRectFor once the per-viewport base unit is known.
internal readonly struct CollectionGridCell
{
    public CollectionGridCell(int col, int shelf, int widthSpan)
    {
        Col = col;
        Shelf = shelf;
        WidthSpan = widthSpan;
    }

    public int Col { get; }
    public int Shelf { get; }
    public int WidthSpan { get; }
}

// Inclusive index range [FirstIndex, LastIndex] of the cards packed into one shelf band. Lets
// the virtualizer map a visible shelf range straight onto a contiguous visible-index window.
internal readonly struct CollectionGridShelf
{
    public CollectionGridShelf(int firstIndex, int lastIndex)
    {
        FirstIndex = firstIndex;
        LastIndex = lastIndex;
    }

    public int FirstIndex { get; }
    public int LastIndex { get; }
}

// Plain pixel rect (overlay space, top-left origin, y increasing downward). Deliberately not
// UnityEngine.Rect so CollectionGridLayout stays unit-testable without a Unity reference.
internal readonly struct CollectionGridRect
{
    public CollectionGridRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }

    public bool Contains(float px, float py) =>
        px >= X && px <= X + Width && py >= Y && py <= Y + Height;
}
