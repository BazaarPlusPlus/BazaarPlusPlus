#nullable enable
namespace BazaarPlusPlus.Core.Ui;

// The game's LetterBoxManager floors the aspect at 16:10. Its fixed vertical field of view
// would crop the board's sides on narrower screens without a wider view.
public static class ScreenFill
{
    public const float ReferenceAspect = 16f / 10f;

    public const int FloorWidth = 1;

    public const int FloorHeight = 1;

    // The native 16:10 view includes side scenery. This zoom trims it while keeping
    // the full board height visible on nearly square screens.
    private const float BoardZoom = 1.3f;

    // The board fills about 97% of the native view, so the HUD needs extra vertical room.
    private const float MinStretch = 1.12f;

    private const float BandGain = 1.05f;

    private const float BottomMargin = 0.012f;

    private const float ClockBandMargin = 0.12f;

    // Leave the clock in its native position when shrinking would make it unreadable.
    private const float ClockMinScale = 0.4f;

    private const float ClockMaxScale = 0.8f;

    // Palm fronds overlap the HUD band in world space, so the clock needs to sit closer to the camera.
    public const float ClockNearer = 0.5f;

    // These reference dimensions match the game's native canvases.
    public const float CanvasReferenceWidth = 3840f;

    public const float CanvasReferenceHeight = 2160f;

    public static float Aspect(float width, float height) => height > 0f ? width / height : 0f;

    public static bool Fills(float aspect) => aspect > 0f && aspect < ReferenceAspect;

    public static bool GathersHudAbove(float aspect) =>
        aspect > 0f && aspect < ReferenceAspect / (MinStretch * BandGain);

    public static float WidenedFieldOfView(float fieldOfView, float aspect)
    {
        if (!Fills(aspect))
            return fieldOfView;
        var halfTan = Math.Tan(fieldOfView * 0.5 * Math.PI / 180.0);
        var stretch = GathersHudAbove(aspect)
            ? Math.Max(MinStretch, ReferenceAspect / (aspect * BoardZoom))
            : ReferenceAspect / aspect;
        return (float)(2.0 * Math.Atan(halfTan * stretch) * 180.0 / Math.PI);
    }

    // World-space overlays built for the native lens need this factor to preserve their screen size.
    public static float ViewStretch(float fieldOfView, float widened) =>
        fieldOfView > 0f
            ? (float)(
                Math.Tan(widened * 0.5 * Math.PI / 180.0)
                / Math.Tan(fieldOfView * 0.5 * Math.PI / 180.0)
            )
            : 1f;

    public static float BoardDrop(float screenHeight, float safeAreaYMin, float vaultBottomFromTop)
    {
        var floor = screenHeight - safeAreaYMin - screenHeight * BottomMargin;
        return floor - vaultBottomFromTop;
    }

    public static float WorldUnitsPerPixel(float depth, float fieldOfView, float screenHeight) =>
        screenHeight > 0f
            ? (float)(2.0 * depth * Math.Tan(fieldOfView * 0.5 * Math.PI / 180.0) / screenHeight)
            : 0f;

    public static (float X, float Y) BoardTopLeft(
        float centerX,
        float centerYFromTop,
        float vaultRight,
        float vaultBottomFromTop
    ) => (2f * centerX - vaultRight, 2f * centerYFromTop - vaultBottomFromTop);

    // The board position uses top-down screen pixels. Unity supplies the clock rectangle and pivot bottom-up.
    public static DayClockPlacement? PlaceDayClock(
        float screenHeight,
        float safeAreaXMin,
        float safeAreaYMax,
        float boardLeft,
        float boardTopFromTop,
        ScreenBox clock,
        float pivotX,
        float pivotY
    )
    {
        if (clock.Height <= 0f)
            return null;
        var boardTop = screenHeight - boardTopFromTop;
        var band = safeAreaYMax - boardTop;
        var height = Math.Min(clock.Height * ClockMaxScale, band * (1f - 2f * ClockBandMargin));
        var scale = height / clock.Height;
        if (!(scale >= ClockMinScale))
            return null;
        var targetX = Math.Max(safeAreaXMin, boardLeft) + clock.Width * scale / 2f;
        var targetY = boardTop + band / 2f;
        return new DayClockPlacement(
            targetX - (clock.CenterX - pivotX) * scale,
            targetY - (clock.CenterY - pivotY) * scale,
            scale
        );
    }

    // Unity's CanvasScaler interpolates the width and height scales in log space.
    public static float CanvasMatch(float aspect)
    {
        var byWidth = Math.Log(aspect / CanvasReferenceWidth);
        var byHeight = Math.Log(1.0 / CanvasReferenceHeight);
        var wanted = Math.Log(aspect / (CanvasReferenceHeight * ReferenceAspect));
        return (float)Math.Clamp((wanted - byWidth) / (byHeight - byWidth), 0.0, 1.0);
    }

    public static float GameCanvasMatch(float aspect, float original) =>
        Fills(aspect) ? CanvasMatch(aspect) : original;

    // Tooltip canvases already fit their content through their own layout.
    public static bool TakesCanvas(
        float referenceWidth,
        float referenceHeight,
        float match,
        string name
    ) =>
        referenceWidth == CanvasReferenceWidth
        && referenceHeight == CanvasReferenceHeight
        && match >= 0.99f
        && !name.Contains("Tooltip", StringComparison.Ordinal);
}

// Unity supplies screen rectangles with y increasing from the bottom.
public readonly record struct ScreenBox(float XMin, float YMin, float Width, float Height)
{
    public float CenterX => XMin + Width / 2f;

    public float CenterY => YMin + Height / 2f;
}

public readonly record struct DayClockPlacement(float PivotX, float PivotY, float Scale);
