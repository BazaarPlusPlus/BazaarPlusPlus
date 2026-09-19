using BazaarPlusPlus.GameInterop.CardPreview;
using Xunit;

namespace NativeCardPreviewHost.Tests;

public sealed class NativeCardPreviewSlotFitMathTests
{
    [Fact]
    public void Fits_visual_bounds_inside_slot_and_centers_the_native_frame()
    {
        Assert.True(
            NativeCardPreviewSlotFitMath.TryResolve(
                visualMinX: -100f,
                visualMinY: -200f,
                visualMaxX: 100f,
                visualMaxY: 200f,
                slotWidth: 60f,
                slotHeight: 60f,
                slotCenterX: 30f,
                slotCenterY: 15f,
                NativeCardPreviewHorizontalAlignment.Center,
                out var fit
            )
        );

        Assert.Equal(0.15f, fit.Scale, 3);
        Assert.Equal(30f, fit.PositionX, 3);
        Assert.Equal(15f, fit.PositionY, 3);
        Assert.Equal(30f, fit.VisibleWidth, 3);
    }

    [Fact]
    public void Compensates_for_an_off_center_native_frame()
    {
        Assert.True(
            NativeCardPreviewSlotFitMath.TryResolve(
                visualMinX: 20f,
                visualMinY: -100f,
                visualMaxX: 120f,
                visualMaxY: 100f,
                slotWidth: 50f,
                slotHeight: 50f,
                slotCenterX: 0f,
                slotCenterY: 0f,
                NativeCardPreviewHorizontalAlignment.Center,
                out var fit
            )
        );

        Assert.Equal(0.25f, fit.Scale, 3);
        Assert.Equal(-17.5f, fit.PositionX, 3);
        Assert.Equal(0f, fit.PositionY, 3);
        Assert.Equal(25f, fit.VisibleWidth, 3);
    }

    [Theory]
    [InlineData(0f, 100f)]
    [InlineData(100f, 0f)]
    public void Rejects_zero_sized_slots(float slotWidth, float slotHeight)
    {
        Assert.False(
            NativeCardPreviewSlotFitMath.TryResolve(
                -10f,
                -10f,
                10f,
                10f,
                slotWidth,
                slotHeight,
                0f,
                0f,
                NativeCardPreviewHorizontalAlignment.Center,
                out _
            )
        );
    }

    [Fact]
    public void Left_alignment_places_visible_frame_at_slot_left_edge()
    {
        Assert.True(
            NativeCardPreviewSlotFitMath.TryResolve(
                visualMinX: 20f,
                visualMinY: -100f,
                visualMaxX: 120f,
                visualMaxY: 100f,
                slotWidth: 80f,
                slotHeight: 50f,
                slotCenterX: 40f,
                slotCenterY: 0f,
                NativeCardPreviewHorizontalAlignment.Left,
                out var fit
            )
        );

        Assert.Equal(0.25f, fit.Scale, 3);
        Assert.Equal(-5f, fit.PositionX, 3);
        Assert.Equal(0f, fit.PositionY, 3);
        Assert.Equal(25f, fit.VisibleWidth, 3);
    }

    [Theory]
    [InlineData(-4f, 18f, 0f, 4f, 22f)]
    [InlineData(12f, 52f, 5f, -7f, 40f)]
    [InlineData(-30f, 90f, -10f, 20f, 120f)]
    public void Visible_artwork_fit_uses_measured_edges_for_all_spans(
        float visibleMinX,
        float visibleMaxX,
        float slotMinX,
        float expectedCorrection,
        float expectedWidth
    )
    {
        Assert.True(
            NativeCardPreviewSlotFitMath.TryResolveVisibleHorizontalFit(
                visibleMinX,
                visibleMaxX,
                slotMinX,
                out var fit
            )
        );

        Assert.Equal(expectedCorrection, fit.PositionCorrection, 3);
        Assert.Equal(expectedWidth, fit.VisibleWidth, 3);
        Assert.Equal(slotMinX, visibleMinX + fit.PositionCorrection, 3);
    }

    [Theory]
    [InlineData(5f, 5f)]
    [InlineData(float.NaN, 10f)]
    public void Visible_artwork_fit_rejects_invalid_bounds(float minX, float maxX)
    {
        Assert.False(
            NativeCardPreviewSlotFitMath.TryResolveVisibleHorizontalFit(
                minX,
                maxX,
                slotMinX: 0f,
                out _
            )
        );
    }
}
