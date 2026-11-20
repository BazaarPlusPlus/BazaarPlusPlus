#nullable enable

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

public readonly struct CollectionGridCell
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

public readonly struct CollectionGridShelf
{
    public CollectionGridShelf(int firstIndex, int lastIndex)
    {
        FirstIndex = firstIndex;
        LastIndex = lastIndex;
    }

    public int FirstIndex { get; }
    public int LastIndex { get; }
}

// Overlay coordinates use a top-left origin with y increasing downward.
public readonly struct CollectionGridRect
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
