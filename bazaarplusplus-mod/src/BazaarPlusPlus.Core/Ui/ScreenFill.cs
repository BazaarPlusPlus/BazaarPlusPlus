#nullable enable
namespace BazaarPlusPlus.Core.Ui;

/// <summary>
/// The geometry of Screen Fill, the Native Adjustment that lets the game fill a landscape screen
/// narrower than 16:10 on a touch host (ADR-0025 decisions 5 and 6). The game's
/// <c>LetterBoxManager</c> clamps the aspect ratio it draws to <c>[16:10, 21:9]</c> and crops
/// everything outside it; a host lowers that floor to <see cref="FloorWidth"/>:<see cref="FloorHeight"/>.
/// Its 3D cameras keep a fixed vertical field of view, so a narrower screen would cut the board's
/// sides: <see cref="WidenedFieldOfView"/> draws the board <see cref="BoardZoom"/> times nearer
/// than 16:10 does. On a screen that <see cref="GathersHudAbove"/>, the board is lowered to the
/// screen's bottom edge and the day clock moves into the band left above it
/// (<see cref="PlaceDayClock"/>). The game's canvases match their width just far enough to stay as
/// wide as on 16:10 (<see cref="CanvasMatch(float)"/>). Everything here is a pure decision on screen
/// pixels in Unity's convention (y up from the bottom) unless a parameter says otherwise; each
/// host reads and writes the game objects.
/// </summary>
public static class ScreenFill
{
    /// <summary>The game's own floor: every layout was made for 16:10 or wider.</summary>
    public const float ReferenceAspect = 16f / 10f;

    /// <summary>The aspect floor a host writes to the letterbox: any landscape screen fills.</summary>
    public const int FloorWidth = 1;

    /// <inheritdoc cref="FloorWidth"/>
    public const int FloorHeight = 1;

    // How much nearer than the 16:10 view the board is drawn. 16:10 frames the board with a band
    // of scenery on each side, about a quarter of its width; this trims most of it, leaving a
    // margin, and still shows the board's full height on a nearly square screen.
    private const float BoardZoom = 1.3f;

    // The least the vertical view grows when the HUD gathers above the board: the board fills
    // about 97% of the game's own view, and this leaves about an eighth of the screen above it
    // for the band. Near 4:3 it sets the zoom rather than BoardZoom does.
    private const float MinStretch = 1.12f;

    // How much larger the band must draw the board than the 16:10 framing for the band to be
    // worth it.
    private const float BandGain = 1.05f;

    // Of the screen's height, what is kept clear under the lowered board.
    private const float BottomMargin = 0.012f;

    // Of the band's height, what is kept clear above and below the day clock.
    private const float ClockBandMargin = 0.12f;

    // Below this the clock is too small to read; it is left where the game put it.
    private const float ClockMinScale = 0.4f;

    // However tall the band, the clock is drawn smaller than the board's own.
    private const float ClockMaxScale = 0.8f;

    /// <summary>
    /// Of its distance from the camera, how far the day clock is drawn: the scenery over the band
    /// (palm fronds) stands above the board; drawn nearer the camera, and smaller by as much, the
    /// clock stays in front of it.
    /// </summary>
    public const float ClockNearer = 0.5f;

    /// <summary>The size the game's own canvases are made at.</summary>
    public const float CanvasReferenceWidth = 3840f;

    /// <inheritdoc cref="CanvasReferenceWidth"/>
    public const float CanvasReferenceHeight = 2160f;

    /// <summary>The aspect ratio of a screen <paramref name="width"/> by <paramref name="height"/>; zero when it has no height.</summary>
    public static float Aspect(float width, float height) => height > 0f ? width / height : 0f;

    /// <summary>Whether a screen of <paramref name="aspect"/> is narrower than 16:10, so Screen Fill changes it.</summary>
    public static bool Fills(float aspect) => aspect > 0f && aspect < ReferenceAspect;

    /// <summary>
    /// Whether a screen of <paramref name="aspect"/> gathers the HUD in a band above the board.
    /// Making that band costs at least <see cref="MinStretch"/> of vertical view, which shrinks
    /// the board more than the 16:10 framing does on a screen wider than about 1.43:1 (3:2, say),
    /// and gains it little just below; so only from about 1.36:1 down (4:3 and squarer) does the
    /// band draw the board at least <see cref="BandGain"/> times larger. Wider, the board keeps
    /// the 16:10 framing and the HUD its usual places beside it.
    /// </summary>
    public static bool GathersHudAbove(float aspect) =>
        aspect > 0f && aspect < ReferenceAspect / (MinStretch * BandGain);

    /// <summary>
    /// The vertical field of view, in degrees, for a perspective camera the game set to
    /// <paramref name="fieldOfView"/> on a screen of <paramref name="aspect"/> that
    /// <see cref="Fills"/>: raised until the horizontal view is <see cref="BoardZoom"/> times
    /// narrower than what 16:10 shows, but by at least <see cref="MinStretch"/> where the HUD
    /// <see cref="GathersHudAbove"/>; elsewhere just enough to show what 16:10 shows. Unchanged
    /// on a screen that does not fill.
    /// </summary>
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

    /// <summary>
    /// How far, in screen pixels, the board must move down so that its bottom edge (the vault's,
    /// <paramref name="vaultBottomFromTop"/> pixels down from the top) sits a margin above the
    /// screen's safe bottom (<paramref name="safeAreaYMin"/>, up from the bottom). Zero or less
    /// when it already sits there or lower.
    /// </summary>
    public static float BoardDrop(float screenHeight, float safeAreaYMin, float vaultBottomFromTop)
    {
        var floor = screenHeight - safeAreaYMin - screenHeight * BottomMargin;
        return floor - vaultBottomFromTop;
    }

    /// <summary>
    /// The world distance one screen pixel spans at <paramref name="depth"/> from a camera with a
    /// vertical field of view of <paramref name="fieldOfView"/> degrees: the camera moves by a
    /// <see cref="BoardDrop"/> times this.
    /// </summary>
    public static float WorldUnitsPerPixel(float depth, float fieldOfView, float screenHeight) =>
        screenHeight > 0f
            ? (float)(2.0 * depth * Math.Tan(fieldOfView * 0.5 * Math.PI / 180.0) / screenHeight)
            : 0f;

    /// <summary>
    /// The board's top-left corner, in screen pixels with y down from the top: the mirror of the
    /// vault's bottom-right corner (<paramref name="vaultRight"/>, <paramref name="vaultBottomFromTop"/>)
    /// through the board's centre (<paramref name="centerX"/>, <paramref name="centerYFromTop"/>).
    /// </summary>
    public static (float X, float Y) BoardTopLeft(
        float centerX,
        float centerYFromTop,
        float vaultRight,
        float vaultBottomFromTop
    ) => (2f * centerX - vaultRight, 2f * centerYFromTop - vaultBottomFromTop);

    /// <summary>
    /// Where the day clock goes in the band between the board's top edge and the safe area's top:
    /// shrunk to fit the band with a margin, never larger than <see cref="ClockMaxScale"/> of its
    /// own size, its left edge level with the board's (or the safe area's, if that is further
    /// right). <paramref name="boardLeft"/> and <paramref name="boardTopFromTop"/> are the
    /// board's top-left corner with y down from the top; <paramref name="clock"/> is the clock's
    /// screen rectangle and <paramref name="pivotX"/>, <paramref name="pivotY"/> its transform's
    /// screen point, both with y up. Null when the clock would be drawn too small to read.
    /// </summary>
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
        // Scaling about the pivot draws the clock's centre that much nearer to it.
        return new DayClockPlacement(
            targetX - (clock.CenterX - pivotX) * scale,
            targetY - (clock.CenterY - pivotY) * scale,
            scale
        );
    }

    /// <summary>
    /// The match at which a 3840×2160 canvas on a screen of <paramref name="aspect"/> is as wide
    /// as on a 16:10 one. CanvasScaler blends the width's and the height's scales in log space,
    /// log s = (1 − m)·log(W/Rw) + m·log(H/Rh); with H = 1 and the scale wanted
    /// s = W/(Rh·16/10), m follows. Between 0 and 1.
    /// </summary>
    public static float CanvasMatch(float aspect)
    {
        var byWidth = Math.Log(aspect / CanvasReferenceWidth);
        var byHeight = Math.Log(1.0 / CanvasReferenceHeight);
        var wanted = Math.Log(aspect / (CanvasReferenceHeight * ReferenceAspect));
        return (float)Math.Clamp((wanted - byWidth) / (byHeight - byWidth), 0.0, 1.0);
    }

    /// <summary>
    /// The match a game canvas gets: <see cref="CanvasMatch(float)"/> on a screen that <see cref="Fills"/>,
    /// else <paramref name="original"/>, the match the game gave it.
    /// </summary>
    public static float GameCanvasMatch(float aspect, float original) =>
        Fills(aspect) ? CanvasMatch(aspect) : original;

    /// <summary>
    /// Whether Screen Fill takes over a game canvas: one made at 3840×2160 that follows the
    /// screen's height (a canvas that already weighs the width keeps what the game chose) and is
    /// not a tooltip's, which is laid out to fit by its own code.
    /// </summary>
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

/// <summary>A rectangle on the screen in pixels, y up from the bottom as Unity reads them.</summary>
public readonly record struct ScreenBox(float XMin, float YMin, float Width, float Height)
{
    public float CenterX => XMin + Width / 2f;

    public float CenterY => YMin + Height / 2f;
}

/// <summary>
/// Where <see cref="ScreenFill.PlaceDayClock"/> puts the day clock: its pivot's screen point (y
/// up) and the scale relative to its own size, before <see cref="ScreenFill.ClockNearer"/>.
/// </summary>
public readonly record struct DayClockPlacement(float PivotX, float PivotY, float Scale);
