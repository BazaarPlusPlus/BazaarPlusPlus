#nullable enable
using System;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Converts the active tab's fixed grid spec into overlay-pixel geometry. Kept Unity-free so the
// preview width contract is testable without booting the game client.
internal readonly struct CollectionGridPixelization
{
    public CollectionGridPixelization(float unit, float originX, float originY, float gridWidth)
    {
        Unit = unit;
        OriginX = originX;
        OriginY = originY;
        GridWidth = gridWidth;
    }

    public float Unit { get; }
    public float OriginX { get; }
    public float OriginY { get; }
    public float GridWidth { get; }

    public static CollectionGridPixelization ForViewport(
        float viewportWidth,
        int columns,
        float maxUnitWidth
    )
    {
        var pad = CollectionGridConstants.GridOuterPadding;
        if (viewportWidth <= 0f)
        {
            var zeroViewportGridWidth = GridWidthFor(
                columns,
                CollectionGridConstants.MinUnitWidth
            );
            return new CollectionGridPixelization(
                CollectionGridConstants.MinUnitWidth,
                pad,
                pad,
                zeroViewportGridWidth
            );
        }

        var availableGridWidth = viewportWidth - 2f * pad;
        var targetGridWidth = Math.Min(availableGridWidth, SharedMaxGridWidth);
        var minGridWidth = GridWidthFor(columns, CollectionGridConstants.MinUnitWidth);
        targetGridWidth = Math.Max(targetGridWidth, minGridWidth);
        var rawUnit =
            (targetGridWidth - (columns - 1) * CollectionGridConstants.GridGap) / columns;
        var unit = Clamp(rawUnit, CollectionGridConstants.MinUnitWidth, maxUnitWidth);
        var gridWidth = GridWidthFor(columns, unit);
        var originX = Math.Max(pad, (viewportWidth - gridWidth) * 0.5f);
        return new CollectionGridPixelization(unit, originX, pad, gridWidth);
    }

    public static float SharedMaxGridWidth =>
        Math.Min(
            GridWidthFor(
                CollectionGridConstants.ItemColumns,
                CollectionGridConstants.ItemMaxUnitWidth
            ),
            GridWidthFor(
                CollectionGridConstants.SkillColumns,
                CollectionGridConstants.SkillMaxUnitWidth
            )
        );

    public static float GridWidthFor(int columns, float unit) =>
        columns * unit + (columns - 1) * CollectionGridConstants.GridGap;

    private static float Clamp(float value, float min, float max) =>
        Math.Max(min, Math.Min(max, value));
}
